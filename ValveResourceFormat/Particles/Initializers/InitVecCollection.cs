namespace ValveResourceFormat.Particles.Initializers
{
    /// <summary>
    /// Sets a vector particle attribute to a value read once for the whole collection, so every
    /// particle spawned in a frame receives the same vector.
    /// </summary>
    /// <remarks>
    /// The collection-scoped counterpart of <see cref="InitVec"/>. It carries neither a set method
    /// nor an input strength, so the value always replaces the attribute.
    ///
    /// <para>The engine reads the input once for a whole batch of new particles. This reads it once
    /// per particle instead, which differs only for an input that does not hold still within a
    /// frame, such as a randomised one.</para>
    /// </remarks>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_INIT_InitVecCollection">C_INIT_InitVecCollection</seealso>
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
