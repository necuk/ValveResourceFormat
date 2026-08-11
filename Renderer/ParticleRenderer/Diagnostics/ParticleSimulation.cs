using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using ValveResourceFormat.Renderer.Particles.Utils;
using ValveResourceFormat.ResourceTypes;

namespace ValveResourceFormat.Renderer.Particles.Diagnostics
{
    /// <summary>Which authored control point configuration a simulation starts from.</summary>
    public enum ParticleControlPointSource
    {
        /// <summary>Every control point starts at its default; only explicit overrides are applied.</summary>
        None,

        /// <summary>
        /// The configuration the effect plays under in game (<c>game</c>, or <c>fps_view</c> for a
        /// viewmodel effect), which is where several constants an effect's operators depend on live.
        /// </summary>
        Runtime,
    }

    /// <summary>One control point placed by the caller, applied after the configuration.</summary>
    /// <param name="Index">The control point number.</param>
    /// <param name="Position">Its world position.</param>
    /// <param name="Orientation">Its forward direction, or zero to leave it unset.</param>
    public readonly record struct ParticleControlPointPlacement(int Index, Vector3 Position, Vector3 Orientation);

    /// <summary>
    /// Everything that decides what a simulation run produces. Two runs with equal options over one
    /// asset produce byte-identical traces; that is the property the whole comparison rests on, and
    /// it is why the seed is a required part of the options rather than an ambient default.
    /// </summary>
    public sealed record ParticleSimulationOptions
    {
        /// <summary>
        /// The root system's offset into the shared random table. In the viewer this is drawn at
        /// random per instance, which is exactly what a comparison cannot have.
        /// </summary>
        public int Seed { get; init; }

        /// <summary>
        /// How far apart consecutive systems' seeds are placed. Each system in the tree gets its own
        /// seed, as the engine gives each instance its own, derived here from the root's so the whole
        /// tree is reproducible from one number.
        /// </summary>
        public int SeedStride { get; init; } = 61;

        /// <summary>The fixed step every frame is advanced by, in seconds.</summary>
        public float TimeStep { get; init; } = 1f / 60f;

        /// <summary>How many steps to run.</summary>
        public int Steps { get; init; } = 120;

        /// <summary>Which authored control point configuration to seed from.</summary>
        public ParticleControlPointSource ControlPointSource { get; init; } = ParticleControlPointSource.Runtime;

        /// <summary>Control points the caller places itself, applied after the configuration.</summary>
        public IReadOnlyList<ParticleControlPointPlacement> ControlPoints { get; init; } = [];

        /// <summary>The detail tier, which decides which child systems run at all.</summary>
        public ParticleDetailLevel DetailLevel { get; init; } = ParticleDetailLevel.PARTICLEDETAIL_ULTRA;

        /// <summary>Whether to record which function changed which fields.</summary>
        public bool RecordFunctions { get; init; } = true;

        /// <summary>Whether to record every particle's attributes at the end of a step.</summary>
        public bool RecordState { get; init; } = true;

        /// <summary>Record state every this many steps. 1 records every step.</summary>
        public int StateStride { get; init; } = 1;
    }

    /// <summary>What a run produced, for the caller's receipt.</summary>
    /// <param name="Systems">Every system in the tree, root first, as <c>path\tname\tseed</c>.</param>
    /// <param name="Steps">How many steps were run.</param>
    /// <param name="Records">How many records were written.</param>
    public sealed record ParticleSimulationResult(IReadOnlyList<string> Systems, int Steps, long Records);

    /// <summary>
    /// Runs a particle system as a simulation alone — no window, no GL context, no renderers — and
    /// writes what it did as a trace.
    /// </summary>
    /// <remarks>
    /// <para>This exists because a particle difference between two implementations is a difference in
    /// state long before it is a difference in pixels, and the state is where it can be attributed to
    /// the function that caused it. Reading it off the screen can only ever say that the effect looks
    /// wrong.</para>
    /// <para>Everything the viewer draws is skipped: the renderers are not constructed, so no texture,
    /// shader or buffer is touched and the run needs no graphics device at all.</para>
    /// </remarks>
    public static class ParticleSimulation
    {
        /// <summary>
        /// Simulates <paramref name="particleSystem"/> and writes the trace to
        /// <paramref name="output"/>.
        /// </summary>
        /// <param name="particleSystem">The effect to run.</param>
        /// <param name="rendererContext">Supplies the file loader child systems are resolved through.</param>
        /// <param name="options">What to run and how to record it.</param>
        /// <param name="output">Where the newline-delimited records go.</param>
        /// <returns>A summary of the run.</returns>
        public static ParticleSimulationResult Run(
            ParticleSystem particleSystem,
            RendererContext rendererContext,
            ParticleSimulationOptions options,
            TextWriter output)
        {
            ArgumentNullException.ThrowIfNull(particleSystem);
            ArgumentNullException.ThrowIfNull(rendererContext);
            ArgumentNullException.ThrowIfNull(options);
            ArgumentNullException.ThrowIfNull(output);

            var writer = new ParticleTraceWriter(output, options.RecordFunctions, options.RecordState, options.StateStride);

            var harness = new ParticleSimulationHarness
            {
                SkipRenderers = true,
                Sink = writer,
            };

            var root = new ParticleRenderer(particleSystem, rendererContext, scene: null, particleSnapshot: null, parentSystemRenderState: null, harness: harness);

            var tree = new List<ParticleRenderer>();
            Walk(root, string.Empty, tree);

            for (var i = 0; i < tree.Count; i++)
            {
                tree[i].State.Random.SetSeed(options.Seed + (i * options.SeedStride));
            }

            root.SetDetailLevel(options.DetailLevel);

            if (options.ControlPointSource == ParticleControlPointSource.Runtime)
            {
                ParticleControlPointDrivers.ApplyRuntimeValues(particleSystem, root.GetControlPoint);
            }

            foreach (var placement in options.ControlPoints)
            {
                var point = root.GetControlPoint(placement.Index);
                point.Position = placement.Position;

                if (placement.Orientation != Vector3.Zero)
                {
                    point.Orientation = placement.Orientation;
                    point.Rotation = null;
                }
            }

            writer.WriteRaw(BuildManifest(particleSystem, options, tree));

            root.Start();

            var worldTime = 0f;

            for (var step = 0; step < options.Steps; step++)
            {
                worldTime += options.TimeStep;
                root.Update(options.TimeStep, worldTime);
            }

            var summary = tree
                .Select(static system => string.Create(CultureInfo.InvariantCulture, $"{system.TracePath}\t{system.Name}\t{system.State.Random.Seed}"))
                .ToArray();

            return new ParticleSimulationResult(summary, options.Steps, writer.RecordCount);
        }

        // Depth-first in definition order, which is also the order the tree is stepped in, so a seed
        // derived from the position here stays put as long as the asset does.
        private static void Walk(ParticleRenderer system, string path, List<ParticleRenderer> tree)
        {
            system.TracePath = path.Length == 0 ? "root" : path;
            tree.Add(system);

            var children = system.ChildSystems;

            for (var i = 0; i < children.Count; i++)
            {
                Walk(children[i], string.Create(CultureInfo.InvariantCulture, $"{system.TracePath}/{i}"), tree);
            }
        }

        private static string BuildManifest(ParticleSystem particleSystem, ParticleSimulationOptions options, List<ParticleRenderer> tree)
        {
            var manifest = new StringBuilder(1024);

            manifest.Append("{\"k\":\"manifest\",\"version\":1");
            manifest.Append(",\"asset\":").Append(Quote(particleSystem.Resource?.FileName ?? "<unnamed>"));
            manifest.Append(",\"seed\":").Append(options.Seed.ToString(CultureInfo.InvariantCulture));
            manifest.Append(",\"seedStride\":").Append(options.SeedStride.ToString(CultureInfo.InvariantCulture));
            manifest.Append(",\"dt\":").Append(ParticleTraceWriter.Format(options.TimeStep));
            manifest.Append(",\"steps\":").Append(options.Steps.ToString(CultureInfo.InvariantCulture));
            manifest.Append(",\"detailLevel\":").Append(Quote(options.DetailLevel.ToString()));
            manifest.Append(",\"controlPointSource\":").Append(Quote(options.ControlPointSource.ToString()));
            manifest.Append(",\"recordFunctions\":").Append(options.RecordFunctions ? "true" : "false");
            manifest.Append(",\"recordState\":").Append(options.RecordState ? "true" : "false");
            manifest.Append(",\"stateStride\":").Append(options.StateStride.ToString(CultureInfo.InvariantCulture));
            manifest.Append(",\"fields\":[");
            manifest.AppendJoin(',', ParticleFields.AllNames.Select(Quote));
            manifest.Append("],\"systems\":[");

            for (var i = 0; i < tree.Count; i++)
            {
                if (i > 0)
                {
                    manifest.Append(',');
                }

                var system = tree[i];

                manifest.Append("{\"path\":").Append(Quote(system.TracePath));
                manifest.Append(",\"name\":").Append(Quote(system.Name));
                manifest.Append(",\"seed\":").Append(system.State.Random.Seed.ToString(CultureInfo.InvariantCulture));
                manifest.Append(",\"behaviorVersion\":").Append(system.BehaviorVersion.ToString(CultureInfo.InvariantCulture));
                manifest.Append(",\"functions\":[");

                var first = true;

                foreach (var (phase, className) in system.DescribeFunctions())
                {
                    if (!first)
                    {
                        manifest.Append(',');
                    }

                    first = false;
                    manifest.Append("{\"phase\":").Append(Quote(phase)).Append(",\"class\":").Append(Quote(className)).Append('}');
                }

                manifest.Append("]}");
            }

            manifest.Append("]}");

            return manifest.ToString();
        }

        private static string Quote(string value)
        {
            var quoted = new StringBuilder(value.Length + 2);
            quoted.Append('"');

            foreach (var c in value)
            {
                switch (c)
                {
                    case '"': quoted.Append("\\\""); break;
                    case '\\': quoted.Append("\\\\"); break;
                    case '\n': quoted.Append("\\n"); break;
                    case '\r': quoted.Append("\\r"); break;
                    case '\t': quoted.Append("\\t"); break;
                    default:
                        if (c < ' ')
                        {
                            quoted.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            quoted.Append(c);
                        }

                        break;
                }
            }

            quoted.Append('"');

            return quoted.ToString();
        }
    }
}
