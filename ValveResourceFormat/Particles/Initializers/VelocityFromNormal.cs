namespace ValveResourceFormat.Particles.Initializers
{
    class VelocityFromNormal : ParticleFunctionInitializer
    {
        private readonly float speedMin;
        private readonly float speedMax;
        private readonly bool ignoreDt;

        public VelocityFromNormal(ParticleDefinitionParser parse) : base(parse)
        {
            speedMin = parse.Float("m_fSpeedMin", speedMin);
            speedMax = parse.Float("m_fSpeedMax", speedMax);
            ignoreDt = parse.Boolean("m_bIgnoreDt", ignoreDt);
        }

        public override ulong WrittenFields => FieldMask(ParticleField.PositionPrevious);

        public override Particle Initialize(ref Particle particle, ParticleCollection particles, ParticleSystemState particleSystemState)
        {
            var speed = particleSystemState.Random.NextBetween(speedMin, speedMax);
            var velocity = particle.Normal * speed;

            if (ignoreDt)
            {
                var frameTime = particleSystemState.Data?.CurrentFrameTime ?? 0f;
                if (frameTime > 0f)
                {
                    velocity /= frameTime;
                }
            }

            particle.Velocity += velocity;

            return particle;
        }
    }
}
