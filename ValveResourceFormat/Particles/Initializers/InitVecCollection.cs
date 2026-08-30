namespace ValveResourceFormat.Particles.Initializers
{
    class InitVecCollection : ParticleFunctionInitializer
    {
        private readonly IVectorProvider inputValue = new LiteralVectorProvider(Vector3.Zero);
        private readonly ParticleField outputField = ParticleField.Color;
        private Vector3 batchValue;
        private bool batchValueResolved;

        public InitVecCollection(ParticleDefinitionParser parse) : base(parse)
        {
            inputValue = parse.VectorProvider("m_InputValue", inputValue);
            outputField = parse.ParticleField("m_nOutputField", outputField);
        }

        public override ulong WrittenFields => FieldMask(outputField);

        public override void BeginInitializeBatch()
        {
            batchValueResolved = false;
        }

        public override Particle Initialize(ref Particle particle, ParticleCollection particles, ParticleSystemState particleSystemState)
        {
            if (!batchValueResolved)
            {
                batchValue = inputValue.NextVector(particleSystemState);
                batchValueResolved = true;
            }

            particle.SetVector(outputField, batchValue);

            return particle;
        }
    }
}
