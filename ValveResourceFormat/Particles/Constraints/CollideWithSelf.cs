namespace ValveResourceFormat.Particles.Constraints
{
    /// <summary>
    /// Pushes particles apart so no two overlap, moving each particle out onto the shell where the two
    /// scaled radii touch. Only the particle being relaxed moves, so a pass over the collection is
    /// order dependent by design. A minimum speed leaves particles that barely moved this step where
    /// they are, and two particles sharing a position have no direction to separate along.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_CollideWithSelf">C_OP_CollideWithSelf</seealso>
    class CollideWithSelf : ParticleFunctionConstraint
    {
        private readonly INumberProvider radiusScale = new LiteralNumberProvider(1f);
        private readonly INumberProvider minimumSpeed = new LiteralNumberProvider(1f);

        /// <summary>An authored literal of zero switches the speed gate off rather than gating on any movement.</summary>
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

            for (var i = 0; i < particles.Count; i++)
            {
                ref var particle = ref particles.Current[i];

                if (checksSpeed)
                {
                    var speed = minimumSpeed.NextNumber(ref particle, particleSystemState);
                    var step = particle.Position - particle.PositionPrevious;

                    if (step.LengthSquared() <= speed * speed * frameTime)
                    {
                        continue;
                    }
                }

                var radius = radiusScale.NextNumber(ref particle, particleSystemState) * particle.Radius;

                for (var j = 0; j < particles.Count; j++)
                {
                    ref var other = ref particles.Current[j];
                    var contact = radius + (radiusScale.NextNumber(ref other, particleSystemState) * other.Radius);
                    var separation = particle.Position - other.Position;
                    var distanceSquared = separation.LengthSquared();

                    if (distanceSquared >= contact * contact || distanceSquared == 0f)
                    {
                        continue;
                    }

                    particle.Position = other.Position + (separation * (contact / MathF.Sqrt(distanceSquared)));
                    moved = true;
                }
            }

            return moved;
        }
    }
}
