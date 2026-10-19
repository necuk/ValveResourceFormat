using System.Threading.Tasks;
using ValveKeyValue;
using ValveResourceFormat;
using ValveResourceFormat.IO;
using ValveResourceFormat.Particles;
using ValveResourceFormat.ResourceTypes;

namespace Tests;

public class ParticleInitialRepulsionVelocityTest
{
    [Test]
    public async Task FactorySupportsTheClass()
    {
        await Assert.That(ParticleSupportInfo.IsInitializerSupported("C_INIT_InitialRepulsionVelocity")).IsTrue();
    }

    [Test]
    public async Task InheritScalesTheControlPointPushBetweenMinAndMax()
    {
        var initializer = Function();
        initializer["m_bInherit"] = new KVObject(true);
        initializer["m_bTranslate"] = new KVObject(true);
        initializer["m_nControlPointNumber"] = new KVObject(11);
        initializer["m_vecOutputMin"] = Vector(1f, 1f, 1f);
        initializer["m_vecOutputMax"] = Vector(3f, 3f, 3f);

        var simulation = Build(initializer);
        simulation.RenderState.SetControlPointValue(11, new Vector3(2f, -4f, 8f));
        simulation.RenderState.SetControlPointValue(12, new Vector3(0.5f, 0f, 0f));
        simulation.Start();

        await Assert.That(simulation.Particles.Current[0].Position).IsEqualTo(new Vector3(4f, -8f, 16f));
        await Assert.That(simulation.Particles.Current[0].PositionPrevious).IsEqualTo(new Vector3(4f, -8f, 16f));
    }

    [Test]
    public async Task AccelerateLeavesThePreviousPosition()
    {
        var initializer = Function();
        initializer["m_bInherit"] = new KVObject(true);
        initializer["m_nControlPointNumber"] = new KVObject(1);

        var simulation = Build(initializer);
        simulation.RenderState.SetControlPointValue(1, new Vector3(2f, 0f, 0f));
        simulation.RenderState.SetControlPointValue(2, new Vector3(1f, 0f, 0f));
        simulation.Start();

        await Assert.That(simulation.Particles.Current[0].Position).IsEqualTo(new Vector3(2f, 0f, 0f));
        await Assert.That(simulation.Particles.Current[0].PositionPrevious).IsEqualTo(Vector3.Zero);
    }

    [Test]
    public async Task NoWorldMeansNoPush()
    {
        var initializer = Function();
        initializer["m_bPerParticle"] = new KVObject(true);
        initializer["m_bTranslate"] = new KVObject(true);

        var simulation = Build(initializer);
        simulation.RenderState.SetControlPointValue(0, new Vector3(1f, 2f, 3f));
        simulation.Start();

        await Assert.That(simulation.Particles.Current[1].Position).IsEqualTo(new Vector3(1f, 2f, 3f));
    }

    [Test]
    public async Task HitBelowPushesUpAndWeakensLaterTracesInTheBatch()
    {
        var initializer = Function();
        initializer["m_bPerParticle"] = new KVObject(true);
        initializer["m_bTranslate"] = new KVObject(true);

        var simulation = Build(initializer);
        var collision = new FloorCollision();
        simulation.RenderState.Collision = collision;
        simulation.Start();

        await Assert.That(simulation.Particles.Current[0].Position).IsEqualTo(new Vector3(0f, 0f, 0.25f));
        await Assert.That(collision.Rays[5].End).IsEqualTo(new Vector3(0f, 0f, -64f));

        await Assert.That(collision.Rays[11].End).IsEqualTo(new Vector3(0f, 0f, 32f));
        await Assert.That(collision.Rays[6].End).IsEqualTo(new Vector3(0f, 0f, 0f));
    }

    [Test]
    public async Task BroadcastReachesDescendantsOfMatchingChildren()
    {
        var initializer = Function();
        initializer["m_nChildCP"] = new KVObject(5);
        initializer["m_nChildGroupID"] = new KVObject(7);

        var grandchild = Definition();
        grandchild["m_nGroupID"] = new KVObject(99);
        var child = Definition();
        child["m_nGroupID"] = new KVObject(7);
        child["m_Children"] = KVObject.Array([Child("grandchild")]);
        var other = Definition();
        other["m_nGroupID"] = new KVObject(8);
        var root = Definition(initializer);
        root["m_Children"] = KVObject.Array([Child("child"), Child("other")]);
        var loader = new ParticleFileLoader();
        loader.Add("child", child);
        loader.Add("grandchild", grandchild);
        loader.Add("other", other);
        var simulation = new ParticleSystemSimulation(ParticleSystem.Create(root), loader);
        var matched = simulation.Children[0];
        var descendant = matched.Children[0];
        var unmatched = simulation.Children[1];

        foreach (var system in new[] { matched, descendant, unmatched })
        {
            system.RenderState.OverrideControlPoint(5).Position = new Vector3(9f, 8f, 7f);
            system.RenderState.OverrideControlPoint(6).Position = new Vector3(6f, 5f, 4f);
        }

        simulation.Start();

        foreach (var system in new[] { matched, descendant })
        {
            await Assert.That(system.RenderState.GetControlPoint(5).Position).IsEqualTo(Vector3.Zero);
            await Assert.That(system.RenderState.GetControlPoint(6).Position).IsEqualTo(Vector3.Zero);
        }

        await Assert.That(unmatched.RenderState.GetControlPoint(5).Position).IsEqualTo(new Vector3(9f, 8f, 7f));
        await Assert.That(unmatched.RenderState.GetControlPoint(6).Position).IsEqualTo(new Vector3(6f, 5f, 4f));
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task SpawnPreservesAnEarlierVelocity(bool translate)
    {
        var velocity = KVObject.Collection();
        velocity["_class"] = new KVObject("C_INIT_VelocityRandom");
        velocity["m_LocalCoordinateSystemSpeedMin"] = Vector(-12f, -12f, -12f);
        velocity["m_LocalCoordinateSystemSpeedMax"] = Vector(-12f, -12f, -12f);
        var initializer = Function();
        initializer["m_bInherit"] = new KVObject(true);
        initializer["m_bTranslate"] = new KVObject(translate);
        initializer["m_nControlPointNumber"] = new KVObject(11);
        initializer["m_vecOutputMin"] = Vector(1f, 1f, 1f);
        initializer["m_vecOutputMax"] = Vector(3f, 3f, 3f);
        var definition = Definition(velocity, initializer);
        var simulation = new ParticleSystemSimulation(ParticleSystem.Create(definition), new NullFileLoader());
        simulation.RenderState.SetControlPointValue(0, new Vector3(1.5f, -3.5f, 2.25f));
        simulation.RenderState.SetControlPointValue(11, new Vector3(2f, -4f, 8f));
        simulation.RenderState.SetControlPointValue(12, new Vector3(0.5f, 0f, 0f));

        simulation.Start();

        await Assert.That(simulation.Particles.Current[0].Position).IsEqualTo(new Vector3(5.5f, -11.5f, 18.25f));
        await Assert.That(simulation.Particles.Current[0].PositionPrevious).IsEqualTo(
            translate ? new Vector3(8.5f, -8.5f, 21.25f) : new Vector3(4.5f, -0.5f, 5.25f));
    }

    [Test]
    public async Task UnevenTraceFractionsMatchTheNativePosition()
    {
        var initializer = Function();
        initializer["m_bPerParticle"] = new KVObject(true);
        initializer["m_bTranslate"] = new KVObject(true);
        var simulation = Build(initializer);
        simulation.RenderState.SetControlPointValue(0, new Vector3(1.5f, -3.5f, 2.25f));
        simulation.RenderState.Collision = new ScriptedCollision([0.125f, 0.5f, 0.75f, 1f, 0.25f, 0.625f]);

        simulation.Start();

        var expected = new Vector3(-3.3369913f, -6.724661f, -2.5869913f);
        await Assert.That(simulation.Particles.Current[0].Position).IsEqualTo(expected);
        await Assert.That(simulation.Particles.Current[0].PositionPrevious).IsEqualTo(expected);
    }

    [Test]
    public async Task OpposingTracePushesCancel()
    {
        var initializer = Function();
        initializer["m_bPerParticle"] = new KVObject(true);
        initializer["m_bTranslate"] = new KVObject(true);
        var simulation = Build(initializer);
        simulation.RenderState.SetControlPointValue(0, new Vector3(1.5f, -3.5f, 2.25f));
        simulation.RenderState.Collision = new ScriptedCollision([0.5f]);

        simulation.Start();

        await Assert.That(simulation.Particles.Current[0].Position).IsEqualTo(new Vector3(1.5f, -3.5f, 2.25f));
        await Assert.That(simulation.Particles.Current[1].Position).IsEqualTo(new Vector3(1.5f, -3.5f, 2.25f));
    }

    [Test]
    public async Task BatchTracePushIsReusedForEveryParticle()
    {
        var initializer = Function();
        initializer["m_bTranslate"] = new KVObject(true);
        var simulation = Build(initializer);
        var collision = new ScriptedCollision([1f, 1f, 1f, 1f, 1f, 0.5f]);
        simulation.RenderState.Collision = collision;

        simulation.Start();

        await Assert.That(simulation.Particles.Current[0].Position).IsEqualTo(new Vector3(0f, 0f, 0.25f));
        await Assert.That(simulation.Particles.Current[1].Position).IsEqualTo(new Vector3(0f, 0f, 0.25f));
        await Assert.That(collision.TraceCount).IsEqualTo(6);
    }

    private static KVObject Child(string name)
    {
        var child = KVObject.Collection();
        child["m_ChildRef"] = new KVObject(name);
        return child;
    }

    private static KVObject Function()
    {
        var result = KVObject.Collection();
        result["_class"] = new KVObject("C_INIT_InitialRepulsionVelocity");
        return result;
    }

    private static KVObject Vector(float x, float y, float z) => KVObject.Array([new KVObject(x), new KVObject(y), new KVObject(z)]);

    private static ParticleSystemSimulation Build(KVObject initializer)
        => new(ParticleSystem.Create(Definition(initializer)), new NullFileLoader());

    private static KVObject Definition(params KVObject[] initializers)
    {
        var definition = KVObject.Collection();
        definition["m_nBehaviorVersion"] = new KVObject(12);
        definition["m_nInitialParticles"] = new KVObject(2);
        definition["m_nMaxParticles"] = new KVObject(2);
        definition["m_flMaximumTimeStep"] = new KVObject(0.25f);
        definition["m_Initializers"] = KVObject.Array(initializers);

        return definition;
    }

    private sealed class ParticleFileLoader : NullFileLoader, IFileLoader
    {
        private readonly Dictionary<string, Resource> resources = [];

        public void Add(string name, KVObject definition)
        {
            var resource = new Resource { FileName = name };
            resource.UnparsedBlocks.Add(ParticleSystem.Create(definition));
            resources.Add(name, resource);
        }

        Resource? IFileLoader.LoadFileCompiled(string file) => resources.GetValueOrDefault(file);
    }

    private sealed class FloorCollision : IParticleCollision
    {
        public List<(Vector3 Start, Vector3 End)> Rays { get; } = [];

        public bool HasGeometry => true;

        public bool TraceRay(Vector3 rayStart, Vector3 rayEnd, out Vector3 position, out Vector3 normal, out float fraction)
        {
            Rays.Add((rayStart, rayEnd));
            fraction = rayEnd.Z < rayStart.Z ? 0.5f : 1f;
            position = Vector3.Lerp(rayStart, rayEnd, fraction);
            normal = fraction < 1f ? Vector3.UnitZ : Vector3.Zero;
            return fraction < 1f;
        }
    }

    private sealed class ScriptedCollision(float[] fractions) : IParticleCollision
    {
        public bool HasGeometry => true;
        public int TraceCount { get; private set; }

        public bool TraceRay(Vector3 rayStart, Vector3 rayEnd, out Vector3 position, out Vector3 normal, out float fraction)
        {
            fraction = fractions[TraceCount++ % fractions.Length];
            position = Vector3.Lerp(rayStart, rayEnd, fraction);
            normal = Vector3.Zero;
            return fraction < 1f;
        }
    }
}
