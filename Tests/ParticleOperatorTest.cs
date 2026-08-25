using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using ValveKeyValue;
using ValveResourceFormat.Particles;
using ValveResourceFormat.Particles.Operators;

namespace Tests;

public class ParticleOperatorTest
{
    [Test]
    public async Task FactorySupportsTheMissingOperatorClasses()
    {
        await Assert.That(ParticleSupportInfo.IsOperatorSupported("C_OP_RadiusDecay")).IsTrue();
        await Assert.That(ParticleSupportInfo.IsOperatorSupported("C_OP_OrientTo2dDirection")).IsTrue();
        await Assert.That(ParticleSupportInfo.IsOperatorSupported("C_OP_DifferencePreviousParticle")).IsTrue();
        await Assert.That(ParticleSupportInfo.IsOperatorSupported("C_OP_DistanceBetweenVecs")).IsTrue();
    }

    [Test]
    public async Task RadiusDecayKillsAtAndBelowTheEngineThreshold()
    {
        var data = FunctionData();
        data["m_flMinRadius"] = new KVObject(1f);
        var particles = Collection(
            new Particle { Radius = 0.5f },
            new Particle { Radius = 1f },
            new Particle { Radius = 1.5f });

        new RadiusDecay(Parser(data)).Operate(particles, 0.1f, new ParticleSystemState(), 0.25f);

        await Assert.That(particles.Current[0].MarkedAsKilled).IsTrue();
        await Assert.That(particles.Current[1].MarkedAsKilled).IsTrue();
        await Assert.That(particles.Current[2].MarkedAsKilled).IsFalse();
    }

    [Test]
    public async Task OrientTo2dDirectionUsesItsSpinStrengthAndLeavesZeroDirectionUntouched()
    {
        var data = FunctionData();
        data["m_vecInput"] = LiteralVector(0f, 1f, 0f);
        data["m_flRotOffset"] = new KVObject(90f);
        data["m_flSpinStrength"] = new KVObject(0.5f);
        data["m_nFieldOutput"] = new KVObject((int)ParticleField.Roll);
        var particles = Collection(new Particle { Rotation = new Vector3(0f, 0f, 0.25f) });

        new OrientTo2dDirection(Parser(data)).Operate(particles, 0.1f, new ParticleSystemState(), 0.1f);

        await Assert.That(particles.Current[0].Rotation.Z)
            .IsEqualTo(float.Lerp(0.25f, MathF.Tau, 0.5f)).Within(1e-6f);

        data["m_vecInput"] = LiteralVector(0f, 0f, 1f);
        particles.Current[0].Rotation = new Vector3(0f, 0f, 0.25f);

        new OrientTo2dDirection(Parser(data)).Operate(particles, 0.1f, new ParticleSystemState(), 1f);

        await Assert.That(particles.Current[0].Rotation.Z).IsEqualTo(0.25f);
    }

    [Test]
    public async Task DifferencePreviousParticleDoesNotAdvancePastAnInactivePair()
    {
        var data = FunctionData();
        data["m_nFieldInput"] = new KVObject((int)ParticleField.Position);
        data["m_nFieldOutput"] = new KVObject((int)ParticleField.Radius);
        data["m_flInputMin"] = new KVObject(0f);
        data["m_flInputMax"] = new KVObject(10f);
        data["m_flOutputMin"] = new KVObject(0f);
        data["m_flOutputMax"] = new KVObject(1f);
        data["m_bActiveRange"] = new KVObject(true);
        data["m_bSetPreviousParticle"] = new KVObject(true);
        var particles = Collection(
            new Particle { Position = Vector3.Zero, Radius = 2f },
            new Particle { Position = new Vector3(100f, 0f, 0f), Radius = 3f },
            new Particle { Position = new Vector3(5f, 0f, 0f), Radius = 4f });

        new DifferencePreviousParticle(Parser(data)).Operate(particles, 0.1f, new ParticleSystemState(), 0.1f);

        await Assert.That(particles.Current[0].Radius).IsEqualTo(1f);
        await Assert.That(particles.Current[1].Radius).IsEqualTo(3f);
        await Assert.That(particles.Current[2].Radius).IsEqualTo(0.5f);
    }

    [Test]
    public async Task DifferencePreviousParticleTreatsTheEngineSentinelAsNoPreviousParticle()
    {
        var data = FunctionData();
        data["m_nFieldInput"] = new KVObject((int)ParticleField.Position);
        data["m_nFieldOutput"] = new KVObject((int)ParticleField.Radius);
        data["m_flInputMin"] = new KVObject(0f);
        data["m_flInputMax"] = new KVObject(128f);
        data["m_flOutputMin"] = new KVObject(0f);
        data["m_flOutputMax"] = new KVObject(1f);
        var sentinel = new Vector3(float.MaxValue);
        var particles = Collection(
            new Particle { Position = sentinel, Radius = 2f },
            new Particle { Position = Vector3.Zero, Radius = 3f },
            new Particle { Position = Vector3.UnitX, Radius = 4f });

        new DifferencePreviousParticle(Parser(data)).Operate(
            particles, 0.1f, new ParticleSystemState(), 1f);

        await Assert.That(particles.Current[0].Radius).IsEqualTo(2f);
        await Assert.That(particles.Current[1].Radius).IsEqualTo(3f);
        await Assert.That(particles.Current[2].Radius).IsEqualTo(1f / 128f);
    }

    [Test]
    public async Task DifferencePreviousParticleClampsNanDistanceToOutputMinimum()
    {
        var data = FunctionData();
        data["m_nFieldInput"] = new KVObject((int)ParticleField.Position);
        data["m_nFieldOutput"] = new KVObject((int)ParticleField.Radius);
        data["m_flInputMin"] = new KVObject(0f);
        data["m_flInputMax"] = new KVObject(128f);
        data["m_flOutputMin"] = new KVObject(10f);
        data["m_flOutputMax"] = new KVObject(20f);
        var particles = Collection(
            new Particle { Position = new Vector3(float.MaxValue, float.NaN, 0f), Radius = 2f },
            new Particle { Position = Vector3.Zero, Radius = 3f });

        new DifferencePreviousParticle(Parser(data)).Operate(
            particles, 0.25f, new ParticleSystemState(), 1f);

        await Assert.That(particles.Current[1].Radius).IsEqualTo(10f);
    }

    [Test]
    public async Task DistanceBetweenVecsUsesUnitSpanForEqualBoundsThenAppliesStrength()
    {
        var data = FunctionData();
        data["m_nFieldOutput"] = new KVObject((int)ParticleField.Radius);
        data["m_vecPoint1"] = LiteralVector(0f, 0f, 0f);
        data["m_vecPoint2"] = LiteralVector(3.5f, 0f, 0f);
        data["m_flInputMin"] = LiteralFloat(3f);
        data["m_flInputMax"] = LiteralFloat(3f);
        data["m_flOutputMin"] = LiteralFloat(10f);
        data["m_flOutputMax"] = LiteralFloat(20f);
        data["m_nSetMethod"] = new KVObject("PARTICLE_SET_ADD_TO_CURRENT_VALUE");
        var particles = Collection(new Particle { Radius = 4f });

        new DistanceBetweenVecs(Parser(data)).Operate(particles, 0.1f, new ParticleSystemState(), 0.25f);

        await Assert.That(particles.Current[0].Radius).IsEqualTo(7.75f);
    }

    [Test]
    public async Task DistanceBetweenVecsDividesDistanceByFrameTime()
    {
        var data = FunctionData();
        data["m_nFieldOutput"] = new KVObject((int)ParticleField.Radius);
        data["m_vecPoint1"] = LiteralVector(0f, 0f, 0f);
        data["m_vecPoint2"] = LiteralVector(1f, 0f, 0f);
        data["m_flInputMin"] = LiteralFloat(0f);
        data["m_flInputMax"] = LiteralFloat(10f);
        data["m_flOutputMin"] = LiteralFloat(0f);
        data["m_flOutputMax"] = LiteralFloat(10f);
        data["m_bDeltaTime"] = new KVObject(true);
        var particles = Collection(new Particle { Radius = 4f });

        new DistanceBetweenVecs(Parser(data)).Operate(particles, 0.5f, new ParticleSystemState(), 1f);

        await Assert.That(particles.Current[0].Radius).IsEqualTo(2f);
    }

    private static ParticleDefinitionParser Parser(KVObject data) => new(data, NullLogger.Instance, 12);

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

    private static KVObject FunctionData() => KVObject.Collection();

    private static KVObject LiteralFloat(float value)
    {
        var input = KVObject.Collection();
        input["m_nType"] = new KVObject("PF_TYPE_LITERAL");
        input["m_flLiteralValue"] = new KVObject(value);
        return input;
    }

    private static KVObject LiteralVector(float x, float y, float z)
    {
        var input = KVObject.Collection();
        input["m_nType"] = new KVObject("PVEC_TYPE_LITERAL");
        input["m_vLiteralValue"] = Vector(x, y, z);
        return input;
    }

    private static KVObject Vector(float x, float y, float z)
        => KVObject.Array([new KVObject(x), new KVObject(y), new KVObject(z)]);
}
