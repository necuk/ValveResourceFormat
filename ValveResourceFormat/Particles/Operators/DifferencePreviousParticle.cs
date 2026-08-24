namespace ValveResourceFormat.Particles.Operators
{
    /// <summary>
    /// Remaps the distance between each particle and the one before it in the collection onto a scalar
    /// attribute. The first particle has no predecessor so it only seeds the walk, and with
    /// <c>m_bActiveRange</c> set a distance outside the input range leaves both particles untouched.
    /// <c>m_bSetPreviousParticle</c> additionally scales the predecessor's output attribute by the same
    /// remapped value.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_DifferencePreviousParticle">C_OP_DifferencePreviousParticle</seealso>
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

            // The two class fields carrying an alpha hold a fraction, so their output range is clamped
            if (fieldOutput is ParticleField.Alpha or ParticleField.AlphaAlternate)
            {
                outputMin = ClampFraction(outputMin);
                outputMax = ClampFraction(outputMax);
            }
        }

        private static float ClampFraction(float value) => value < 0f ? 0f : MathF.Min(1f, value);

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength)
        {
            var hasPrevious = false;
            var previousValue = Vector3.Zero;
            var previousIndex = 0;

            for (var i = 0; i < particles.Count; i++)
            {
                ref var particle = ref particles.Current[i];
                var value = particle.GetVector(fieldInput);

                if (hasPrevious)
                {
                    var distance = Vector3.Distance(previousValue, value);

                    if (activeRange && (distance < inputMin || distance > inputMax))
                    {
                        continue;
                    }

                    var output = MathUtils.RemapValClamped(distance, inputMin, inputMax, outputMin, outputMax);

                    particle.SetScalar(fieldOutput, particle.ModifyScalarBySetMethod(particles, fieldOutput, output, setMethod));

                    if (setPreviousParticle)
                    {
                        ref var previous = ref particles.Current[previousIndex];
                        previous.SetScalar(fieldOutput, previous.GetScalar(fieldOutput) * output);
                    }
                }

                hasPrevious = true;
                previousValue = value;
                previousIndex = i;
            }
        }
    }
}
