using System.IO;
using System.Linq;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using ValveResourceFormat;
using ValveResourceFormat.IO;
using ValveResourceFormat.Renderer;
using ValveResourceFormat.Renderer.Particles.Diagnostics;
using ValveResourceFormat.ResourceTypes;

namespace Tests.Renderer
{
    /// <summary>
    /// Locks the properties a particle comparison rests on. Two implementations of an effect can
    /// only be compared if one of them can be run twice and produce the same thing, so these are
    /// the tests that have to hold before any divergence found against another implementation
    /// means anything at all.
    /// </summary>
    /// <remarks>
    /// The whole run happens with no GL context: the renderers are never constructed, which is what
    /// makes a particle system testable on a build agent at all.
    /// </remarks>
    public class ParticleSimulationTest
    {
        // A real shipped effect with emitters, initializers and operators, small enough to run in
        // a test. Its children are not in the fixture set, so this exercises one system.
        private const string Fixture = "explosion_barrel_kv0_lz4.vpcf_c";

        private readonly List<IDisposable> disposables = [];

        [TearDown]
        public void DisposeHarness()
        {
            for (var i = disposables.Count - 1; i >= 0; i--)
            {
                disposables[i].Dispose();
            }

            disposables.Clear();
        }

        private string RunTrace(ParticleSimulationOptions options)
        {
            var path = Path.Combine(TestContext.CurrentContext.TestDirectory, "Files", Fixture);

            using var resource = new Resource();
            resource.Read(path);

            var fileLoader = new GameFileLoader(null, null);
            disposables.Add(fileLoader);

            var context = new RendererContext(fileLoader, NullLogger.Instance);
            disposables.Add(context);

            Assert.That(resource.DataBlock, Is.InstanceOf<ParticleSystem>());

            using var output = new StringWriter();
            var result = ParticleSimulation.Run((ParticleSystem)resource.DataBlock!, context, options, output);

            Assert.That(result.Systems, Is.Not.Empty);
            Assert.That(result.Records, Is.GreaterThan(1), "a run that recorded only its manifest simulated nothing");

            return output.ToString();
        }

        private static ParticleSimulationOptions Options(int seed) => new()
        {
            Seed = seed,
            Steps = 20,
            TimeStep = 1f / 60f,
        };

        [Test]
        public void SameSeedProducesTheSameTrace()
        {
            var first = RunTrace(Options(0));
            var second = RunTrace(Options(0));

            Assert.That(second, Is.EqualTo(first), "the same seed and schedule must replay exactly");
        }

        [Test]
        public void DifferentSeedProducesADifferentTrace()
        {
            var first = RunTrace(Options(0));
            var second = RunTrace(Options(7));

            Assert.That(second, Is.Not.EqualTo(first), "the seed must still decide what the effect does");

            // The schedule is the same, so the shape of the trace must not move with the seed: a
            // difference in record counts would mean the seed changed what ran, not what it drew.
            Assert.That(CountRecords(second, "\"k\":\"step\""), Is.EqualTo(CountRecords(first, "\"k\":\"step\"")));
        }

        [Test]
        public void EveryStepIsRecordedForEverySystem()
        {
            const int steps = 20;
            var trace = RunTrace(Options(0) with { Steps = steps });

            var stepRecords = trace
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Count(line => line.Contains("\"k\":\"step\"", StringComparison.Ordinal));

            // Pre-simulation, when the effect has any, runs extra steps before the first update.
            Assert.That(stepRecords, Is.GreaterThanOrEqualTo(steps));
        }

        [Test]
        public void ManifestNamesTheAssetAndTheRunSettings()
        {
            var trace = RunTrace(Options(3) with { Steps = 2 });
            var manifest = trace.Split('\n')[0];

            Assert.That(manifest, Does.Contain("\"k\":\"manifest\""));
            Assert.That(manifest, Does.Contain("\"seed\":3"));
            Assert.That(manifest, Does.Contain("\"steps\":2"));
            Assert.That(manifest, Does.Contain("\"fields\":["));
            Assert.That(manifest, Does.Contain("\"systems\":["));
        }

        [Test]
        public void FunctionRecordsNameTheAuthoredClass()
        {
            var trace = RunTrace(Options(0) with { Steps = 5 });

            // The attribution layer is only useful if it names the engine's class; a record naming
            // our implementing type would point at the wrong operator whenever several share one.
            Assert.That(trace, Does.Contain("\"class\":\"C_"));
        }

        [Test]
        public void FunctionRecordsCanBeTurnedOff()
        {
            var trace = RunTrace(Options(0) with { Steps = 5, RecordFunctions = false });

            Assert.That(trace, Does.Not.Contain("\"k\":\"fn\""));
            Assert.That(trace, Does.Not.Contain("\"k\":\"init\""));
            Assert.That(trace, Does.Contain("\"k\":\"step\""));
        }

        [Test]
        public void StateRecordsCanBeTurnedOff()
        {
            var trace = RunTrace(Options(0) with { Steps = 5, RecordState = false });

            Assert.That(trace, Does.Not.Contain("\"k\":\"p\""));
            Assert.That(trace, Does.Contain("\"k\":\"step\""));
        }

        [Test]
        public void ControlPointPlacementReachesTheSimulation()
        {
            var placed = RunTrace(Options(0) with
            {
                Steps = 5,
                ControlPoints = [new ParticleControlPointPlacement(0, new Vector3(100f, 200f, 300f), Vector3.Zero)],
            });

            var atOrigin = RunTrace(Options(0) with { Steps = 5 });

            Assert.That(placed, Is.Not.EqualTo(atOrigin), "moving control point 0 must move the effect");
            Assert.That(placed, Does.Contain("\"k\":\"cp\""));
        }

        private static int CountRecords(string trace, string marker)
            => trace.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Count(line => line.Contains(marker, StringComparison.Ordinal));
    }
}
