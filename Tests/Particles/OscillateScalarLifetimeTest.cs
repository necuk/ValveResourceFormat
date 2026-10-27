using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using ValveKeyValue;
using ValveResourceFormat.Particles;
using ValveResourceFormat.Particles.Operators;
using ValveResourceFormat.Utils;

namespace Tests.Particles
{
    public class OscillateScalarLifetimeTest
    {
        [Test]
        public async Task TinyLifetimeUsesTheActualAgeFraction()
        {
            var definition = new KVObject
            {
                ["m_nField"] = 3,
                ["m_bProportional"] = true,
                ["m_bProportionalOp"] = true,
                ["m_RateMin"] = 1f,
                ["m_RateMax"] = 1f,
                ["m_FrequencyMin"] = 1f,
                ["m_FrequencyMax"] = 1f,
                ["m_flStartTime_min"] = 0f,
                ["m_flStartTime_max"] = 0f,
                ["m_flEndTime_min"] = 2f,
                ["m_flEndTime_max"] = 2f,
                ["m_flOscMult"] = 1f,
                ["m_flOscAdd"] = 0f,
            };
            var operation = new OscillateScalar(new ParticleDefinitionParser(definition, NullLogger.Instance, new()));
            var particles = new ParticleCollection(new ParticleBuffer.ConstantValues(), 1) { Time = 9.53674316E-07f };
            particles.SetStorage(ulong.MaxValue, 0, 0);
            particles.Add();
            particles.Current.Floats(ParticleField.LifeDuration)[0] = 1.90734863E-06f;
            particles.Current.Floats(ParticleField.Radius)[0] = 1f;

            operation.Operate(particles, 0.05f, new ParticleSystemState(), 1f);

            await Assert.That(particles.Current.ReadFloat(ParticleField.Radius, 0)).IsEqualTo(1.05f);
        }
    }
}
