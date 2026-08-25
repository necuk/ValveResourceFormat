namespace ValveResourceFormat.Particles.Operators
{
    class DifferencePreviousParticle : ParticleFunctionOperator
    {
        private readonly ParticleField fieldInput = ParticleField.Position;
        private readonly ParticleField fieldOutput = ParticleField.Radius;
        private readonly float inputMin;
        private readonly float inputMax = 128f;
        private readonly float outputMin;
        private readonly float outputMax = 1f;
        private readonly ParticleSetMethod setMethod = ParticleSetMethod.PARTICLE_SET_REPLACE_VALUE;
        private readonly bool activeRange;
        private readonly bool setPreviousParticle;

        public DifferencePreviousParticle(ParticleDefinitionParser parse) : base(parse)
        {
            fieldInput = parse.ParticleField("m_nFieldInput", fieldInput);
            fieldOutput = parse.ParticleField("m_nFieldOutput", fieldOutput);
            inputMin = parse.Float("m_flInputMin", inputMin);
            inputMax = parse.Float("m_flInputMax", inputMax);
            outputMin = parse.Float("m_flOutputMin", outputMin);
            outputMax = parse.Float("m_flOutputMax", outputMax);
            setMethod = parse.Enum<ParticleSetMethod>("m_nSetMethod", setMethod);
            activeRange = parse.Boolean("m_bActiveRange", activeRange);
            setPreviousParticle = parse.Boolean("m_bSetPreviousParticle", setPreviousParticle);

            if (fieldOutput is ParticleField.Alpha or ParticleField.AlphaAlternate)
            {
                outputMin = ClampFraction(outputMin);
                outputMax = ClampFraction(outputMax);
            }
        }

        private static float ClampFraction(float value) => value < 0f ? 0f : MathF.Min(1f, value);

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength)
        {
            var previousValue = new Vector3(float.MaxValue);
            var previousIndex = 0;

            for (var i = 0; i < particles.Count; i++)
            {
                ref var particle = ref particles.Current[i];
                var value = particle.GetVector(fieldInput);

                if (previousValue.X != float.MaxValue
                    || previousValue.Y != float.MaxValue
                    || previousValue.Z != float.MaxValue)
                {
                    var distance = Vector3.Distance(previousValue, value);

                    if (activeRange && (distance < inputMin || distance > inputMax))
                    {
                        continue;
                    }

                    var output = float.IsNaN(distance)
                        ? outputMin
                        : MathUtils.RemapValClamped(distance, inputMin, inputMax, outputMin, outputMax);

                    particle.SetScalar(fieldOutput, particle.ModifyScalarBySetMethod(particles, fieldOutput, output, setMethod));

                    if (setPreviousParticle)
                    {
                        ref var previous = ref particles.Current[previousIndex];
                        previous.SetScalar(fieldOutput, previous.GetScalar(fieldOutput) * output);
                    }
                }

                previousValue = value;
                previousIndex = i;
            }
        }
    }
}
