using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace ValveResourceFormat.Particles.Constraints
{
    class CollideWithSelf : ParticleFunctionConstraint
    {
        private readonly INumberProvider radiusScale = new LiteralNumberProvider(1f);
        private readonly INumberProvider minimumSpeed = new LiteralNumberProvider(1f);

        private readonly bool checksSpeed;

        public CollideWithSelf(ParticleDefinitionParser parse) : base(parse)
        {
            radiusScale = parse.NumberProvider("m_flRadiusScale", radiusScale);
            minimumSpeed = parse.NumberProvider("m_flMinimumSpeed", minimumSpeed);
            checksSpeed = minimumSpeed is not LiteralNumberProvider { Value: <= 0f };
        }

        public override bool ApplyConstraint(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState)
        {
            var moved = false;
            Span<Vector3> positions = stackalloc Vector3[4];
            Span<float> radii = stackalloc float[4];

            for (var blockStart = 0; blockStart < particles.Count; blockStart += 4)
            {
                var laneCount = Math.Min(4, particles.Count - blockStart);

                if (checksSpeed && !BlockPassesSpeedGate(particles, blockStart, laneCount, frameTime, particleSystemState))
                {
                    continue;
                }

                for (var lane = 0; lane < laneCount; lane++)
                {
                    ref var particle = ref particles.Current[blockStart + lane];
                    positions[lane] = particle.Position;
                    radii[lane] = radiusScale.NextNumber(ref particle, particleSystemState) * particle.Radius;
                }

                for (var j = 0; j < particles.Count; j++)
                {
                    ref var other = ref particles.Current[j];
                    var otherPosition = other.Position;
                    var otherRadius = radiusScale.NextNumber(ref other, particleSystemState) * other.Radius;

                    for (var lane = 0; lane < laneCount; lane++)
                    {
                        var contact = radii[lane] + otherRadius;
                        var separation = positions[lane] - otherPosition;
                        var distanceSquared = separation.X * separation.X;
                        distanceSquared += separation.Y * separation.Y;
                        distanceSquared += separation.Z * separation.Z;

                        if (distanceSquared >= contact * contact)
                        {
                            continue;
                        }

                        moved = true;
                        var reciprocalLength = EngineReciprocalSquareRoot(distanceSquared);
                        positions[lane] = new Vector3(
                            ScaleAndAdd(separation.X, reciprocalLength, contact, otherPosition.X),
                            ScaleAndAdd(separation.Y, reciprocalLength, contact, otherPosition.Y),
                            ScaleAndAdd(separation.Z, reciprocalLength, contact, otherPosition.Z));
                    }

                    for (var lane = 0; lane < laneCount; lane++)
                    {
                        particles.Current[blockStart + lane].Position = positions[lane];
                    }
                }
            }

            return moved;
        }

        private static float EngineReciprocalSquareRoot(float squaredLength)
        {
            var bits = BitConverter.SingleToUInt32Bits(squaredLength);
            var magnitude = BitConverter.UInt32BitsToSingle(bits & 0x7FFFFFFFu);
            if (magnitude < BitConverter.UInt32BitsToSingle(0x00800000u))
            {
                bits |= 0x34000000u;
            }

            var safeSquaredLength = BitConverter.UInt32BitsToSingle(bits);
            if (!Sse.IsSupported)
            {
                throw new PlatformNotSupportedException("Particle self collision requires SSE reciprocal-square-root support.");
            }

            var estimate = Sse.ReciprocalSqrt(Vector128.Create(safeSquaredLength)).GetElement(0);
            var estimateSquared = estimate * estimate;
            var scaledLength = squaredLength * estimateSquared;
            var correction = 3f - scaledLength;
            correction *= estimate;
            correction *= 0.5f;
            return correction;
        }

        private static float ScaleAndAdd(float separation, float reciprocalLength, float contact, float origin)
        {
            var normalized = separation * reciprocalLength;
            var offset = normalized * contact;
            return offset + origin;
        }

        private bool BlockPassesSpeedGate(ParticleCollection particles, int blockStart, int laneCount, float frameTime, ParticleSystemState particleSystemState)
        {
            var passes = false;

            for (var lane = 0; lane < laneCount; lane++)
            {
                ref var particle = ref particles.Current[blockStart + lane];
                var speed = minimumSpeed.NextNumber(ref particle, particleSystemState);
                var step = particle.Position - particle.PositionPrevious;

                if (step.LengthSquared() > speed * speed * frameTime)
                {
                    passes = true;
                }
            }

            return passes;
        }
    }
}
