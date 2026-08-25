namespace ValveResourceFormat.Particles.Operators
{
    class OrientTo2dDirection : ParticleFunctionOperator
    {
        private readonly IVectorProvider input = new ParticleVelocityVectorProvider();
        private readonly float rotOffset;
        private readonly float spinStrength = 1f;
        private readonly ParticleField fieldOutput = ParticleField.Roll;

        public OrientTo2dDirection(ParticleDefinitionParser parse) : base(parse)
        {
            input = parse.VectorProvider("m_vecInput", input);
            rotOffset = parse.Float("m_flRotOffset", rotOffset);
            spinStrength = parse.Float("m_flSpinStrength", spinStrength);
            fieldOutput = parse.ParticleField("m_nFieldOutput", fieldOutput);
        }

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength)
        {
            var offset = float.DegreesToRadians(rotOffset);

            foreach (ref var particle in particles.Current)
            {
                var direction = input.NextVector(ref particle, particleSystemState);

                if (direction.X == 0f && direction.Y == 0f)
                {
                    continue;
                }

                var angle = MathF.Atan2(direction.Y, direction.X) + MathF.PI + offset;
                var current = particle.GetScalar(fieldOutput);

                particle.SetScalar(fieldOutput, float.Lerp(current, angle, spinStrength));
            }
        }
    }
}
