namespace ValveResourceFormat.Particles.Initializers
{
    class ScaleVelocity : ParticleFunctionInitializer
    {
        private readonly IVectorProvider scale = new LiteralVectorProvider(Vector3.One);
        private Vector3 batchScale;
        private bool batchScaleResolved;

        public ScaleVelocity(ParticleDefinitionParser parse) : base(parse)
        {
            scale = parse.VectorProvider("m_vecScale", scale);
        }

        public override ulong WrittenFields => FieldMask(ParticleField.PositionPrevious);

        public override void BeginInitializeBatch()
        {
            batchScaleResolved = false;
        }

        public override Particle Initialize(ref Particle particle, ParticleCollection particles, ParticleSystemState particleSystemState)
        {
            if (!batchScaleResolved)
            {
                batchScale = scale.NextVector(particleSystemState);
                batchScaleResolved = true;
            }

            particle.Velocity *= batchScale;

            return particle;
        }
    }
}
