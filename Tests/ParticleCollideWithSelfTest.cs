using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using ValveKeyValue;
using ValveResourceFormat.Particles;
using ValveResourceFormat.Particles.Constraints;

namespace Tests;

public class ParticleCollideWithSelfTest
{
    [Test]
    public async Task FactorySupportsCollideWithSelf()
    {
        await Assert.That(ParticleSupportInfo.IsConstraintSupported("C_OP_CollideWithSelf")).IsTrue();
    }

    [Test]
    public async Task StagesTheParticleBlockBeforeAdvancingToTheNextNeighbor()
    {
        var data = KVObject.Collection();
        data["m_flRadiusScale"] = LiteralFloat(1f);
        data["m_flMinimumSpeed"] = LiteralFloat(1f);
        var particles = Collection(
            new Particle { Position = new Vector3(0f, 0f, 0f), PositionPrevious = new Vector3(-0.4f, 0f, 0f), Radius = 1f },
            new Particle { Position = new Vector3(1f, 0f, 0f), PositionPrevious = new Vector3(0.6f, 0f, 0f), Radius = 1f });

        var moved = new CollideWithSelf(new ParticleDefinitionParser(data, NullLogger.Instance, 12))
            .ApplyConstraint(particles, 0.1f, new ParticleSystemState());

        await Assert.That(moved).IsTrue();
        await Assert.That(particles.Current[0].Position).IsEqualTo(new Vector3(0f, 0f, 0f));
        await Assert.That(particles.Current[1].Position).IsEqualTo(new Vector3(2f, 0f, 0f));
    }

    [Test]
    public async Task ProcessesAFourParticleBlockWhenAnyLanePassesTheSpeedGate()
    {
        var data = KVObject.Collection();
        data["m_flRadiusScale"] = LiteralFloat(1f);
        data["m_flMinimumSpeed"] = LiteralFloat(1f);
        var particles = Collection(
            new Particle { Position = new Vector3(0f, 0f, 0f), PositionPrevious = new Vector3(-0.4f, 0f, 0f), Radius = 1f },
            new Particle { Position = new Vector3(1f, 0f, 0f), PositionPrevious = new Vector3(0.9f, 0f, 0f), Radius = 1f },
            new Particle { Position = new Vector3(10f, 0f, 0f), PositionPrevious = new Vector3(10f, 0f, 0f), Radius = 1f },
            new Particle { Position = new Vector3(20f, 0f, 0f), PositionPrevious = new Vector3(20f, 0f, 0f), Radius = 1f });

        var moved = new CollideWithSelf(new ParticleDefinitionParser(data, NullLogger.Instance, 12))
            .ApplyConstraint(particles, 0.1f, new ParticleSystemState());

        await Assert.That(moved).IsTrue();
        await Assert.That(particles.Current[0].Position).IsEqualTo(new Vector3(0f, 0f, 0f));
        await Assert.That(particles.Current[1].Position).IsEqualTo(new Vector3(2f, 0f, 0f));
        await Assert.That(particles.Current[2].Position).IsEqualTo(new Vector3(10f, 0f, 0f));
        await Assert.That(particles.Current[3].Position).IsEqualTo(new Vector3(20f, 0f, 0f));
    }

    [Test]
    public async Task SpeedGateComparesSquaredStepToSpeedSquaredTimesFrameTime()
    {
        var data = KVObject.Collection();
        data["m_flRadiusScale"] = LiteralFloat(1f);
        data["m_flMinimumSpeed"] = LiteralFloat(1f);
        var particles = Collection(
            new Particle { Position = Vector3.Zero, PositionPrevious = new Vector3(-0.2f, 0f, 0f), Radius = 1f },
            new Particle { Position = Vector3.UnitX, PositionPrevious = new Vector3(0.8f, 0f, 0f), Radius = 1f });

        var moved = new CollideWithSelf(new ParticleDefinitionParser(data, NullLogger.Instance, 12))
            .ApplyConstraint(particles, 0.1f, new ParticleSystemState());

        await Assert.That(moved).IsFalse();
        await Assert.That(particles.Current[0].Position).IsEqualTo(Vector3.Zero);
        await Assert.That(particles.Current[1].Position).IsEqualTo(Vector3.UnitX);
    }

    [Test]
    public async Task LiteralZeroDisablesTheSpeedGate()
    {
        var data = KVObject.Collection();
        data["m_flRadiusScale"] = LiteralFloat(1f);
        data["m_flMinimumSpeed"] = LiteralFloat(0f);
        var particles = Collection(
            new Particle { Position = Vector3.Zero, PositionPrevious = Vector3.Zero, Radius = 1f },
            new Particle { Position = Vector3.UnitX, PositionPrevious = Vector3.UnitX, Radius = 1f });

        var moved = new CollideWithSelf(new ParticleDefinitionParser(data, NullLogger.Instance, 12))
            .ApplyConstraint(particles, 0.1f, new ParticleSystemState());

        await Assert.That(moved).IsTrue();
        await Assert.That(particles.Current[0].Position).IsEqualTo(Vector3.Zero);
        await Assert.That(particles.Current[1].Position).IsEqualTo(new Vector3(2f, 0f, 0f));
    }

    [Test]
    public async Task AZeroDistanceMaskStillReportsRelaxation()
    {
        var data = KVObject.Collection();
        data["m_flRadiusScale"] = LiteralFloat(1f);
        data["m_flMinimumSpeed"] = LiteralFloat(0f);
        var particles = Collection(new Particle { Position = Vector3.Zero, PositionPrevious = Vector3.Zero, Radius = 1f });

        var moved = new CollideWithSelf(new ParticleDefinitionParser(data, NullLogger.Instance, 12))
            .ApplyConstraint(particles, 0.1f, new ParticleSystemState());

        await Assert.That(moved).IsTrue();
        await Assert.That(particles.Current[0].Position).IsEqualTo(Vector3.Zero);
    }

    [Test]
    public async Task UsesEngineReciprocalSquareRootBitsForObliqueSeparation()
    {
        var data = KVObject.Collection();
        data["m_flRadiusScale"] = LiteralFloat(1f);
        data["m_flMinimumSpeed"] = LiteralFloat(0f);
        var particles = Collection(
            new Particle { Position = Vector3.Zero, PositionPrevious = Vector3.Zero, Radius = 1f },
            new Particle
            {
                Position = new Vector3(1.2345f, 0.4567f, 0.1111f),
                PositionPrevious = new Vector3(1.2345f, 0.4567f, 0.1111f),
                Radius = 1f,
            });

        var moved = new CollideWithSelf(new ParticleDefinitionParser(data, NullLogger.Instance, 12))
            .ApplyConstraint(particles, 0.1f, new ParticleSystemState());

        await Assert.That(moved).IsTrue();
        await Assert.That(BitConverter.SingleToUInt32Bits(particles.Current[0].Position.X)).IsEqualTo(0x00000000u);
        await Assert.That(BitConverter.SingleToUInt32Bits(particles.Current[0].Position.Y)).IsEqualTo(0x00000000u);
        await Assert.That(BitConverter.SingleToUInt32Bits(particles.Current[0].Position.Z)).IsEqualTo(0x00000000u);
        await Assert.That(BitConverter.SingleToUInt32Bits(particles.Current[1].Position.X)).IsEqualTo(0x3FEF3EFCu);
        await Assert.That(BitConverter.SingleToUInt32Bits(particles.Current[1].Position.Y)).IsEqualTo(0x3F310451u);
        await Assert.That(BitConverter.SingleToUInt32Bits(particles.Current[1].Position.Z)).IsEqualTo(0x3E2C3FD8u);
    }

    [Test]
    public async Task SubnormalDistanceUsesTheEngineSafeMagnitudeBits()
    {
        var data = KVObject.Collection();
        data["m_flRadiusScale"] = LiteralFloat(1f);
        data["m_flMinimumSpeed"] = LiteralFloat(0f);
        var particles = Collection(
            new Particle { Position = Vector3.Zero, PositionPrevious = Vector3.Zero, Radius = 1f },
            new Particle
            {
                Position = new Vector3(1e-20f, 2e-20f, -3e-20f),
                PositionPrevious = new Vector3(1e-20f, 2e-20f, -3e-20f),
                Radius = 1f,
            });

        var moved = new CollideWithSelf(new ParticleDefinitionParser(data, NullLogger.Instance, 12))
            .ApplyConstraint(particles, 0.1f, new ParticleSystemState());

        await Assert.That(moved).IsTrue();
        await Assert.That(BitConverter.SingleToUInt32Bits(particles.Current[0].Position.X)).IsEqualTo(0xBF08D677u);
        await Assert.That(BitConverter.SingleToUInt32Bits(particles.Current[0].Position.Y)).IsEqualTo(0xBF88D677u);
        await Assert.That(BitConverter.SingleToUInt32Bits(particles.Current[0].Position.Z)).IsEqualTo(0x3FCD41B3u);
        await Assert.That(BitConverter.SingleToUInt32Bits(particles.Current[1].Position.X)).IsEqualTo(0x24BD66E5u);
        await Assert.That(BitConverter.SingleToUInt32Bits(particles.Current[1].Position.Y)).IsEqualTo(0x253D66E5u);
        await Assert.That(BitConverter.SingleToUInt32Bits(particles.Current[1].Position.Z)).IsEqualTo(0xA58E0D2Cu);
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

    private static KVObject LiteralFloat(float value)
    {
        var input = KVObject.Collection();
        input["m_nType"] = new KVObject("PF_TYPE_LITERAL");
        input["m_flLiteralValue"] = new KVObject(value);
        return input;
    }
}
