using System.Globalization;
using System.IO;
using System.Text;

namespace ValveResourceFormat.Renderer.Particles.Diagnostics
{
    /// <summary>
    /// Writes the simulation out as newline-delimited JSON, one record per line, so two runs can be
    /// compared field by field without either side having to be rendered.
    /// </summary>
    /// <remarks>
    /// <para>Records carry a <c>k</c> discriminator:</para>
    /// <list type="bullet">
    /// <item><c>fn</c> — one function of one step, with the set of fields it changed and how many
    /// particles it changed each on. This is the attribution layer: a field that first differs at a
    /// step was last written by the last <c>fn</c> record of that step naming it.</item>
    /// <item><c>init</c> — one initializer for one spawning particle, with the fields it wrote.</item>
    /// <item><c>cp</c> — a control point as the step left it.</item>
    /// <item><c>step</c> — the system's own clock and particle count at the end of a step.</item>
    /// <item><c>p</c> — one live particle at the end of a step, every attribute in
    /// <see cref="ParticleFields"/>.</item>
    /// </list>
    /// <para>Floats are written round-trippable, so a reader recovers the exact bits the simulation
    /// held. Non-finite values are written as the bare <c>NaN</c>/<c>Infinity</c> tokens JSON itself
    /// does not define but every reader this is meant for accepts; they are a divergence signal and
    /// must not be silently mapped to null.</para>
    /// </remarks>
    internal sealed class ParticleTraceWriter : ParticleSimulationSink
    {
        /// <summary>The most particle ids one <c>fn</c> record lists before it stops naming them.</summary>
        private const int MaxListedIds = 64;

        private sealed class SystemState
        {
            public Particle[] Previous = [];
            public int PreviousCount;
            public int Step = -1;
            public float FrameTime;
        }

        private readonly TextWriter output;
        private readonly bool writeFunctions;
        private readonly bool writeState;
        private readonly int stateStride;
        private readonly Dictionary<ParticleRenderer, SystemState> systems = [];
        private readonly StringBuilder line = new(512);
        private readonly List<string> changedFields = [];
        private readonly Dictionary<string, int> changeCounts = [];
        private readonly List<int> changedIds = [];

        /// <summary>How many records have been written, for the caller's summary.</summary>
        public long RecordCount { get; private set; }

        /// <summary>
        /// Initializes a writer over <paramref name="output"/>.
        /// </summary>
        /// <param name="output">Where the records go. Not disposed by the writer.</param>
        /// <param name="writeFunctions">Whether to record the per-function attribution layer.</param>
        /// <param name="writeState">Whether to record the per-particle state at the end of each step.</param>
        /// <param name="stateStride">Record state every this many steps; 1 records every step.</param>
        public ParticleTraceWriter(TextWriter output, bool writeFunctions = true, bool writeState = true, int stateStride = 1)
        {
            ArgumentNullException.ThrowIfNull(output);
            ArgumentOutOfRangeException.ThrowIfLessThan(stateStride, 1);

            this.output = output;
            this.writeFunctions = writeFunctions;
            this.writeState = writeState;
            this.stateStride = stateStride;
        }

        /// <summary>Writes a record the simulation driver composed itself, such as the manifest.</summary>
        public void WriteRaw(string json)
        {
            output.Write(json);
            output.Write('\n');
            RecordCount++;
        }

        /// <inheritdoc/>
        public override void StepBegin(ParticleRenderer system, float frameTime)
        {
            var state = StateOf(system);
            state.Step++;
            state.FrameTime = frameTime;
            Snapshot(system, state);
        }

        /// <inheritdoc/>
        public override void FunctionRan(ParticleRenderer system, ParticleFunctionPhase phase, int index, string className)
        {
            var state = StateOf(system);

            if (!writeFunctions)
            {
                Snapshot(system, state);
                return;
            }

            var current = system.CurrentParticles;
            changeCounts.Clear();
            changedIds.Clear();

            var shared = Math.Min(state.PreviousCount, current.Length);

            for (var i = 0; i < shared; i++)
            {
                changedFields.Clear();
                ParticleFields.CollectChangedFields(state.Previous[i], current[i], changedFields);

                if (changedFields.Count == 0)
                {
                    continue;
                }

                foreach (var field in changedFields)
                {
                    changeCounts.TryGetValue(field, out var count);
                    changeCounts[field] = count + 1;
                }

                if (changedIds.Count < MaxListedIds)
                {
                    changedIds.Add(current[i].UniqueParticleId);
                }
            }

            var spawned = current.Length - state.PreviousCount;

            if (changeCounts.Count > 0 || spawned != 0)
            {
                line.Clear();
                line.Append("{\"k\":\"fn\",\"sys\":");
                AppendString(system.TracePath);
                line.Append(",\"step\":").Append(state.Step);
                line.Append(",\"phase\":\"").Append(PhaseName(phase)).Append('"');
                line.Append(",\"i\":").Append(index);
                line.Append(",\"class\":");
                AppendString(className);
                line.Append(",\"spawned\":").Append(spawned);
                line.Append(",\"w\":{");

                var first = true;
                foreach (var (field, count) in changeCounts)
                {
                    if (!first)
                    {
                        line.Append(',');
                    }

                    first = false;
                    AppendString(field);
                    line.Append(':').Append(count.ToString(CultureInfo.InvariantCulture));
                }

                line.Append("},\"ids\":[");

                for (var i = 0; i < changedIds.Count; i++)
                {
                    if (i > 0)
                    {
                        line.Append(',');
                    }

                    line.Append(changedIds[i].ToString(CultureInfo.InvariantCulture));
                }

                line.Append("]}");
                Flush();
            }

            Snapshot(system, state);
        }

        /// <inheritdoc/>
        public override void InitializerRan(ParticleRenderer system, int index, string className, in Particle before, in Particle after)
        {
            if (!writeFunctions)
            {
                return;
            }

            changedFields.Clear();
            ParticleFields.CollectChangedFields(before, after, changedFields);

            if (changedFields.Count == 0)
            {
                return;
            }

            var state = StateOf(system);

            line.Clear();
            line.Append("{\"k\":\"init\",\"sys\":");
            AppendString(system.TracePath);
            line.Append(",\"step\":").Append(state.Step);
            line.Append(",\"uid\":").Append(after.UniqueParticleId);
            line.Append(",\"i\":").Append(index);
            line.Append(",\"class\":");
            AppendString(className);
            line.Append(",\"w\":[");

            for (var i = 0; i < changedFields.Count; i++)
            {
                if (i > 0)
                {
                    line.Append(',');
                }

                AppendString(changedFields[i]);
            }

            line.Append("]}");
            Flush();
        }

        /// <inheritdoc/>
        public override void StepEnd(ParticleRenderer system)
        {
            var state = StateOf(system);
            var particles = system.CurrentParticles;

            line.Clear();
            line.Append("{\"k\":\"step\",\"sys\":");
            AppendString(system.TracePath);
            line.Append(",\"step\":").Append(state.Step);
            line.Append(",\"age\":").Append(Format(system.State.Age));
            line.Append(",\"dt\":").Append(Format(state.FrameTime));
            line.Append(",\"count\":").Append(particles.Length);
            line.Append(",\"seed\":").Append(system.State.Random.Seed);
            line.Append('}');
            Flush();

            if (!writeState || state.Step % stateStride != 0)
            {
                Snapshot(system, state);
                return;
            }

            foreach (var (index, point) in system.State.TracedControlPoints)
            {
                line.Clear();
                line.Append("{\"k\":\"cp\",\"sys\":");
                AppendString(system.TracePath);
                line.Append(",\"step\":").Append(state.Step);
                line.Append(",\"cp\":").Append(index);
                AppendVector(",\"pos\":", point.Position);
                AppendVector(",\"prev\":", point.PositionPrevious);
                AppendVector(",\"ori\":", point.Orientation);
                line.Append('}');
                Flush();
            }

            for (var i = 0; i < particles.Length; i++)
            {
                WriteParticle(system, state.Step, particles[i]);
            }

            Snapshot(system, state);
        }

        private void WriteParticle(ParticleRenderer system, int step, in Particle particle)
        {
            line.Clear();
            line.Append("{\"k\":\"p\",\"sys\":");
            AppendString(system.TracePath);
            line.Append(",\"step\":").Append(step);
            line.Append(",\"uid\":").Append(particle.UniqueParticleId);

            foreach (var field in ParticleFields.Floats)
            {
                line.Append(',');
                AppendString(field.Name);
                line.Append(':').Append(Format(field.Read(particle)));
            }

            foreach (var field in ParticleFields.Vectors)
            {
                line.Append(',');
                AppendString(field.Name);
                AppendVector(":", field.Read(particle));
            }

            foreach (var field in ParticleFields.Ints)
            {
                line.Append(',');
                AppendString(field.Name);
                line.Append(':').Append(field.Read(particle).ToString(CultureInfo.InvariantCulture));
            }

            line.Append('}');
            Flush();
        }

        private void AppendVector(string prefix, Vector3 value)
        {
            line.Append(prefix).Append('[')
                .Append(Format(value.X)).Append(',')
                .Append(Format(value.Y)).Append(',')
                .Append(Format(value.Z)).Append(']');
        }

        private void AppendString(string value)
        {
            line.Append('"');

            foreach (var c in value)
            {
                switch (c)
                {
                    case '"': line.Append("\\\""); break;
                    case '\\': line.Append("\\\\"); break;
                    case '\n': line.Append("\\n"); break;
                    case '\r': line.Append("\\r"); break;
                    case '\t': line.Append("\\t"); break;
                    default:
                        if (c < ' ')
                        {
                            line.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            line.Append(c);
                        }

                        break;
                }
            }

            line.Append('"');
        }

        private void Flush()
        {
            output.Write(line);
            output.Write('\n');
            RecordCount++;
        }

        private SystemState StateOf(ParticleRenderer system)
        {
            if (!systems.TryGetValue(system, out var state))
            {
                state = new SystemState();
                systems.Add(system, state);
            }

            return state;
        }

        private static void Snapshot(ParticleRenderer system, SystemState state)
        {
            var current = system.CurrentParticles;

            if (state.Previous.Length < current.Length)
            {
                Array.Resize(ref state.Previous, Math.Max(current.Length, system.ParticleCapacity));
            }

            current.CopyTo(state.Previous);
            state.PreviousCount = current.Length;
        }

        private static string PhaseName(ParticleFunctionPhase phase) => phase switch
        {
            ParticleFunctionPhase.PreEmission => "preemission",
            ParticleFunctionPhase.Initializer => "initializer",
            ParticleFunctionPhase.Emitter => "emitter",
            ParticleFunctionPhase.Operator => "operator",
            ParticleFunctionPhase.Constraint => "constraint",
            _ => "unknown",
        };

        /// <summary>
        /// Formats a float so the exact bits come back on the other side. Non-finite values keep their
        /// JSON5 spelling rather than becoming null, because losing them would hide a divergence.
        /// </summary>
        internal static string Format(float value)
            => value.ToString("R", CultureInfo.InvariantCulture);
    }
}
