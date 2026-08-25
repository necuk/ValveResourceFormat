using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using ValveKeyValue;
using ValveResourceFormat.IO;
using ValveResourceFormat.Particles;
using ValveResourceFormat.Particles.Initializers;
using ValveResourceFormat.ResourceTypes;

namespace Tests;

public class ParticleInitializerTest
{
    [Test]
    public async Task FactorySupportsTheVelocityInitializerClasses()
    {
        await Assert.That(ParticleSupportInfo.IsInitializerSupported("C_INIT_VelocityFromNormal")).IsTrue();
        await Assert.That(ParticleSupportInfo.IsInitializerSupported("C_INIT_ScaleVelocity")).IsTrue();
        await Assert.That(ParticleSupportInfo.IsInitializerSupported("C_INIT_InitVecCollection")).IsTrue();
    }

    [Test]
    public async Task CollectionScopedVectorInputIsEvaluatedOnceForTheSpawnBatch()
    {
        var initializer = Function("C_INIT_InitVecCollection");
        initializer["m_InputValue"] = RandomVectorInput();

        var simulation = BuildSimulation(initializer);

        simulation.Start();

        await Assert.That(simulation.Particles.Count).IsEqualTo(2);
        await Assert.That(simulation.Particles.Current[1].Color)
            .IsEqualTo(simulation.Particles.Current[0].Color);
    }

    [Test]
    public async Task CollectionScopedInputIsEvaluatedAgainForTheNextSpawnBatch()
    {
        var data = Function("C_INIT_InitVecCollection");
        data["m_InputValue"] = RandomVectorInput();
        var initializer = new InitVecCollection(
            new ParticleDefinitionParser(data, NullLogger.Instance, 12));
        var particles = new ParticleCollection(new Particle(), 3);
        particles.Add();
        particles.Add();
        particles.Add();
        var state = new ParticleSystemState();

        initializer.BeginInitializeBatch();
        initializer.Initialize(ref particles.Current[0], particles, state);
        initializer.Initialize(ref particles.Current[1], particles, state);
        initializer.BeginInitializeBatch();
        initializer.Initialize(ref particles.Current[2], particles, state);

        await Assert.That(particles.Current[1].Color).IsEqualTo(particles.Current[0].Color);
        await Assert.That(particles.Current[2].Color).IsNotEqualTo(particles.Current[0].Color);
    }

    [Test]
    public async Task CollectionScopedFloatInputIsEvaluatedOnceForTheSpawnBatch()
    {
        var input = KVObject.Collection();
        input["m_nType"] = new KVObject("PF_TYPE_RANDOM_UNIFORM");
        input["m_nRandomMode"] = new KVObject("PF_RANDOM_MODE_VARYING");
        input["m_flRandomMin"] = new KVObject(0f);
        input["m_flRandomMax"] = new KVObject(1f);

        var initializer = Function("C_INIT_InitFloatCollection");
        initializer["m_InputValue"] = input;

        var simulation = BuildSimulation(initializer);

        simulation.Start();

        await Assert.That(simulation.Particles.Current[1].Radius)
            .IsEqualTo(simulation.Particles.Current[0].Radius);
    }

    [Test]
    public async Task CollectionScopedVelocityScaleIsEvaluatedOnceForTheSpawnBatch()
    {
        var velocityInput = KVObject.Collection();
        velocityInput["m_nType"] = new KVObject("PVEC_TYPE_LITERAL");
        velocityInput["m_vLiteralValue"] = Vector(2f, 3f, 4f);

        var velocity = Function("C_INIT_VelocityFromCP");
        velocity["m_velocityInput"] = velocityInput;

        var scale = Function("C_INIT_ScaleVelocity");
        scale["m_vecScale"] = RandomVectorInput();

        var simulation = BuildSimulation(velocity, scale);

        simulation.Start();

        await Assert.That(simulation.Particles.Current[1].Velocity)
            .IsEqualTo(simulation.Particles.Current[0].Velocity);
    }

    [Test]
    public async Task VelocityFromNormalIgnoreDtAuthorsRawStepDisplacement()
    {
        var initializer = Function("C_INIT_VelocityFromNormal");
        initializer["m_fSpeedMin"] = new KVObject(3f);
        initializer["m_fSpeedMax"] = new KVObject(3f);
        initializer["m_bIgnoreDt"] = new KVObject(true);

        var definition = KVObject.Collection();
        definition["m_nBehaviorVersion"] = new KVObject(12);
        definition["m_nInitialParticles"] = new KVObject(1);
        definition["m_nMaxParticles"] = new KVObject(1);
        definition["m_flMaximumTimeStep"] = new KVObject(0.25f);
        definition["m_ConstantNormal"] = Vector(0f, 2f, 0f);
        definition["m_Initializers"] = KVObject.Array([initializer]);
        var simulation = new ParticleSystemSimulation(
            ParticleSystem.Create(definition), new NullFileLoader());

        simulation.Start();

        await Assert.That(simulation.Particles.Current[0].Velocity).IsEqualTo(new Vector3(0f, 24f, 0f));
        await Assert.That(simulation.Particles.Current[0].PositionPrevious).IsEqualTo(new Vector3(0f, -6f, 0f));
    }

    private static ParticleSystemSimulation BuildSimulation(params KVObject[] initializers)
    {
        var definition = KVObject.Collection();
        definition["m_nBehaviorVersion"] = new KVObject(12);
        definition["m_nInitialParticles"] = new KVObject(2);
        definition["m_nMaxParticles"] = new KVObject(2);
        definition["m_Initializers"] = KVObject.Array(initializers);

        return new ParticleSystemSimulation(
            ParticleSystem.Create(definition), new NullFileLoader());
    }

    private static KVObject Function(string className)
    {
        var function = KVObject.Collection();
        function["_class"] = new KVObject(className);
        return function;
    }

    private static KVObject RandomVectorInput()
    {
        var input = KVObject.Collection();
        input["m_nType"] = new KVObject("PVEC_TYPE_RANDOM_UNIFORM");
        input["m_vRandomMin"] = Vector(0f, 0f, 0f);
        input["m_vRandomMax"] = Vector(1f, 1f, 1f);
        return input;
    }

    private static KVObject Vector(float x, float y, float z)
        => KVObject.Array([new KVObject(x), new KVObject(y), new KVObject(z)]);
}
