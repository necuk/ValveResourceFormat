namespace ValveResourceFormat.Particles.Operators
{
    class RadiusDecay : ParticleFunctionOperator
    {
        private readonly float minRadius = 1f;

        public RadiusDecay(ParticleDefinitionParser parse) : base(parse)
        {
            minRadius = parse.Float("m_flMinRadius", minRadius);
        }

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength)
        {
            foreach (ref var particle in particles.Current)
            {
                if (particle.Radius <= minRadius)
                {
                    particle.Kill();
                }
            }
        }
    }
}
