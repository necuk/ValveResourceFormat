namespace ValveResourceFormat.Particles.Initializers
{
    /// <summary>
    /// Scales each component of a new particle's velocity by an authored vector. The scale is a
    /// collection level input, so it is read once for the whole spawn batch.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_INIT_ScaleVelocity">C_INIT_ScaleVelocity</seealso>
    class ScaleVelocity : ParticleFunctionInitializer
    {
        private readonly IVectorProvider scale = new LiteralVectorProvider(Vector3.One);

        public ScaleVelocity(ParticleDefinitionParser parse) : base(parse)
        {
            scale = parse.VectorProvider("m_vecScale", scale);
        }

        public override ulong WrittenFields => FieldMask(ParticleField.PositionPrevious);

        public override Particle Initialize(ref Particle particle, ParticleCollection particles, ParticleSystemState particleSystemState)
        {
            particle.Velocity *= scale.NextVector(particleSystemState);

            return particle;
        }
    }
}
