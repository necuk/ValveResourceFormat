using System.Linq;

namespace ValveResourceFormat.Renderer.Particles.Diagnostics
{
    /// <summary>
    /// Every per-particle attribute a trace carries, as one table. Both the state records and the
    /// per-function write sets read it, so a field can never be compared but not attributed, or
    /// attributed under one name and compared under another.
    /// </summary>
    /// <remarks>
    /// Comparison is on the bit pattern rather than on the value: a divergence that only shows in the
    /// sign of a zero, or in which NaN was produced, is still a divergence, and the equality operator
    /// would swallow both.
    /// </remarks>
    internal static class ParticleFields
    {
        /// <summary>One attribute, named as the trace names it and read straight off the struct.</summary>
        internal readonly record struct FloatField(string Name, Func<Particle, float> Read);

        /// <inheritdoc cref="FloatField"/>
        internal readonly record struct VectorField(string Name, Func<Particle, Vector3> Read);

        /// <inheritdoc cref="FloatField"/>
        internal readonly record struct IntField(string Name, Func<Particle, int> Read);

        /// <summary>The scalar attributes, in the order the trace writes them.</summary>
        public static readonly FloatField[] Floats =
        [
            new("Age", static p => p.Age),
            new("Lifetime", static p => p.Lifetime),
            new("Alpha", static p => p.Alpha),
            new("AlphaAlternate", static p => p.AlphaAlternate),
            new("Radius", static p => p.Radius),
            new("TrailLength", static p => p.TrailLength),
            new("ForceScale", static p => p.ForceScale),
            new("AlphaWindowThreshold", static p => p.AlphaWindowThreshold),
            new("ScratchFloat0", static p => p.ScratchFloat0),
            new("ScratchFloat1", static p => p.ScratchFloat1),
            new("ScratchFloat2", static p => p.ScratchFloat2),
            new("BoxFlags", static p => p.BoxFlags),
            new("CreationTime", static p => p.CreationTime),
        ];

        /// <summary>The vector attributes, in the order the trace writes them.</summary>
        public static readonly VectorField[] Vectors =
        [
            new("Position", static p => p.Position),
            new("PositionPrevious", static p => p.PositionPrevious),
            new("Velocity", static p => p.Velocity),
            new("Color", static p => p.Color),
            new("Rotation", static p => p.Rotation),
            new("RotationSpeed", static p => p.RotationSpeed),
            new("Normal", static p => p.Normal),
            new("ForceAccumulator", static p => p.ForceAccumulator),
            new("BoxMins", static p => p.BoxMins),
            new("BoxMaxs", static p => p.BoxMaxs),
            new("BoxAngles", static p => p.BoxAngles),
            new("RopeSegmentData", static p => p.RopeSegmentData),
            new("HitboxOffsetPosition", static p => p.HitboxOffsetPosition),
            new("ScratchVector", static p => p.ScratchVector),
            new("ScratchVector2", static p => p.ScratchVector2),
        ];

        /// <summary>The integral attributes, in the order the trace writes them.</summary>
        public static readonly IntField[] Ints =
        [
            new("UniqueParticleId", static p => p.UniqueParticleId),
            new("ParticleId", static p => p.ParticleId),
            new("Index", static p => p.Index),
            new("SequenceNumber", static p => p.SequenceNumber),
            new("SecondSequenceNumber", static p => p.SecondSequenceNumber),
            new("ManualAnimationFrame", static p => p.ManualAnimationFrame),
            new("ParentParticleIndex", static p => p.ParentParticleIndex),
            new("ParentParticleId", static p => p.ParentParticleId),
            new("RopeSegmentId", static p => p.RopeSegmentId),
            new("UserEventStates", static p => p.UserEventStates),
            new("MarkedAsKilled", static p => p.MarkedAsKilled ? 1 : 0),
        ];

        /// <summary>Every field name the trace can carry, in record order.</summary>
        public static IEnumerable<string> AllNames
            => Floats.Select(static f => f.Name)
                .Concat(Vectors.Select(static f => f.Name))
                .Concat(Ints.Select(static f => f.Name));

        /// <summary>
        /// Appends the names of the fields that differ between two states of one particle. Floats
        /// compare by bit pattern, so a sign-of-zero or NaN-payload difference counts.
        /// </summary>
        public static void CollectChangedFields(in Particle before, in Particle after, ICollection<string> changed)
        {
            ArgumentNullException.ThrowIfNull(changed);

            foreach (var field in Floats)
            {
                if (!SameBits(field.Read(before), field.Read(after)))
                {
                    changed.Add(field.Name);
                }
            }

            foreach (var field in Vectors)
            {
                var a = field.Read(before);
                var b = field.Read(after);

                if (!SameBits(a.X, b.X) || !SameBits(a.Y, b.Y) || !SameBits(a.Z, b.Z))
                {
                    changed.Add(field.Name);
                }
            }

            foreach (var field in Ints)
            {
                if (field.Read(before) != field.Read(after))
                {
                    changed.Add(field.Name);
                }
            }
        }

        private static bool SameBits(float a, float b)
            => BitConverter.SingleToInt32Bits(a) == BitConverter.SingleToInt32Bits(b);
    }
}
