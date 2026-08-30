using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using ValveKeyValue;
using ValveResourceFormat.Particles;
using ValveResourceFormat.Particles.ForceGenerators;

namespace Tests;

public class ParticleTimeVaryingForceTest
{
    [Test]
    public async Task FactorySupportsTimeVaryingForce()
    {
        await Assert.That(ParticleSupportInfo.IsForceGeneratorSupported("C_OP_TimeVaryingForce")).IsTrue();
    }

    [Test]
    public async Task HoldsBothEndpointsAndInterpolatesParticleAge()
    {
        var data = KVObject.Collection();
        data["m_flStartLerpTime"] = new KVObject(2f);
        data["m_flEndLerpTime"] = new KVObject(8f);
        data["m_StartingForce"] = Vector(1f, 2f, 3f);
        data["m_EndingForce"] = Vector(7f, 8f, 9f);
        var particles = Collection(
            new Particle { Age = 0f, ForceAccumulator = Vector3.One },
            new Particle { Age = 5f },
            new Particle { Age = 10f });

        new TimeVaryingForce(new ParticleDefinitionParser(data, NullLogger.Instance, 12))
            .GenerateForces(particles, 0.1f, new ParticleSystemState(), 0.5f);

        await Assert.That(particles.Current[0].ForceAccumulator).IsEqualTo(new Vector3(1.5f, 2f, 2.5f));
        await Assert.That(particles.Current[1].ForceAccumulator).IsEqualTo(new Vector3(2f, 2.5f, 3f));
        await Assert.That(particles.Current[2].ForceAccumulator).IsEqualTo(new Vector3(3.5f, 4f, 4.5f));
    }

    private static ParticleCollection Collection(params Particle[] values)
    {
        var particles = new ParticleCollection(new Particle(), values.Length);
        for (var i = 0; i < values.Length; i++)
        {
            particles.Add();
            values[i].Index = i;
            particles.Current[i] = values[i];
            particles.Initial[i] = values[i];
        }

        return particles;
    }

    private static KVObject Vector(float x, float y, float z)
        => KVObject.Array([new KVObject(x), new KVObject(y), new KVObject(z)]);
}
