using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using ValveResourceFormat.Renderer.PostProcess;
using ValveResourceFormat.Renderer.SceneEnvironment;

namespace Tests
{
    /// <summary>
    /// Numerical characterization of the CPU auto-exposure chain in
    /// <see cref="PostProcessRenderer"/>, plus a stage-timeline emitter used by the
    /// external first-divergence comparison tooling.
    ///
    /// These tests describe what the code currently does. They deliberately do not
    /// assert what the engine does — no engine oracle is wired in here.
    /// </summary>
    public class ExposureTimelineTests
    {
        private const string ScenarioFile = "exposure_scenarios.json";
        private const string TimelineSchema = "vrf_exposure.timeline.v1";

        private sealed record ScenarioFrame(float AverageLuminance, float DeltaTime, ExposureSettings? Settings);

        private sealed record Scenario(string Name, string Description, ExposureSettings Settings, float ExposureBias, IReadOnlyList<ScenarioFrame> Frames);

        private static string ScenarioPath => Path.Combine(TestContext.TestDirectory!, "Files", ScenarioFile);

        private static ExposureSettings ReadSettings(JsonElement element, ExposureSettings fallback)
        {
            float Get(string name, float def) => element.TryGetProperty(name, out var v) ? v.GetSingle() : def;

            var settings = new ExposureSettings
            {
                ExposureMin = Get("exposureMin", fallback.ExposureMin),
                ExposureMax = Get("exposureMax", fallback.ExposureMax),
                ExposureSpeedUp = Get("exposureSpeedUp", fallback.ExposureSpeedUp),
                ExposureSpeedDown = Get("exposureSpeedDown", fallback.ExposureSpeedDown),
                ExposureSmoothingRange = Get("exposureSmoothingRange", fallback.ExposureSmoothingRange),
                ExposureCompensation = Get("exposureCompensation", fallback.ExposureCompensation),
            };

            settings.AutoExposureEnabled = element.TryGetProperty("autoExposureEnabled", out var auto)
                ? auto.GetBoolean()
                : fallback.AutoExposureEnabled;

            return settings;
        }

        private static (IReadOnlyList<Scenario> Scenarios, string Sha256) LoadScenarios()
        {
            var path = ScenarioPath;
            if (!File.Exists(path))
            {
                throw new FileNotFoundException($"scenario corpus missing: {path}", path);
            }

            var bytes = File.ReadAllBytes(path);
            var sha = Convert.ToHexStringLower(SHA256.HashData(bytes));

            using var document = JsonDocument.Parse(bytes);
            var root = document.RootElement;
            var schema = root.GetProperty("schema").GetString();
            if (schema != "vrf_exposure.scenarios.v1")
            {
                throw new InvalidDataException($"scenario corpus schema is {schema}, expected vrf_exposure.scenarios.v1");
            }

            var defaults = new ExposureSettings();
            var scenarios = new List<Scenario>();

            foreach (var scenarioElement in root.GetProperty("scenarios").EnumerateArray())
            {
                var settings = ReadSettings(scenarioElement.GetProperty("settings"), defaults);
                var frames = new List<ScenarioFrame>();

                foreach (var frameElement in scenarioElement.GetProperty("frames").EnumerateArray())
                {
                    ExposureSettings? overrideSettings = frameElement.TryGetProperty("settings", out var frameSettings)
                        ? ReadSettings(frameSettings, settings)
                        : null;

                    frames.Add(new ScenarioFrame(
                        frameElement.GetProperty("averageLuminance").GetSingle(),
                        frameElement.GetProperty("deltaTime").GetSingle(),
                        overrideSettings));
                }

                var settingsElement = scenarioElement.GetProperty("settings");
                var exposureBias = settingsElement.TryGetProperty("exposureBias", out var bias) ? bias.GetSingle() : 0.0f;

                scenarios.Add(new Scenario(
                    scenarioElement.GetProperty("name").GetString()!,
                    scenarioElement.TryGetProperty("description", out var d) ? d.GetString()! : string.Empty,
                    settings,
                    exposureBias,
                    frames));
            }

            return (scenarios, sha);
        }

        private static List<ExposureFrame> RunMirror(Scenario scenario)
        {
            var mirror = new ExposureMirror();
            var records = new List<ExposureFrame>(scenario.Frames.Count);

            for (var i = 0; i < scenario.Frames.Count; i++)
            {
                var frame = scenario.Frames[i];
                var settings = frame.Settings ?? scenario.Settings;
                records.Add(mirror.Step(i, settings, frame.AverageLuminance, frame.DeltaTime));
            }

            return records;
        }

        /// <summary>
        /// The mirror used to expose intermediate stages must reproduce the real renderer
        /// bit-exactly at every publicly observable boundary, on every scenario frame.
        /// If this fails, every timeline emitted by this fixture is invalid.
        /// </summary>
        [Test]
        public async Task MirrorMatchesRenderer()
        {
            var (scenarios, _) = LoadScenarios();
            await Assert.That(scenarios).IsNotEmpty();

            foreach (var scenario in scenarios)
            {
                var renderer = new PostProcessRenderer(null!);
                var mirrored = RunMirror(scenario);

                for (var i = 0; i < scenario.Frames.Count; i++)
                {
                    var frame = scenario.Frames[i];
                    var settings = frame.Settings ?? scenario.Settings;

                    renderer.State = PostProcessState.Default with { ExposureSettings = settings };
                    renderer.AverageLuminance = frame.AverageLuminance;
                    renderer.CalculateTonemapScalar(frame.DeltaTime);

                    var expected = mirrored[i];
                    var where = $"{scenario.Name}[{i}]";

                    await AssertBitExact(renderer.TonemapScalar, expected.TonemapScalar, $"{where} TonemapScalar");
                    await AssertBitExact(renderer.CurrentExposure, expected.CurrentExposure, $"{where} CurrentExposure");
                    await AssertBitExact(renderer.TargetExposure, expected.TargetExposure, $"{where} TargetExposure");

                    await Assert.That(renderer.ExposureHistory.Count).IsEqualTo(expected.History.Length)
                        .Because($"{where} ExposureHistory length");
                    for (var h = 0; h < expected.History.Length; h++)
                    {
                        await AssertBitExact(renderer.ExposureHistory[h], expected.History[h], $"{where} ExposureHistory[{h}]");
                    }
                }
            }
        }

        private static async Task AssertBitExact(float actual, float expected, string where)
        {
            await Assert.That(BitConverter.SingleToInt32Bits(actual))
                .IsEqualTo(BitConverter.SingleToInt32Bits(expected))
                .Because($"{where}: actual {actual} vs expected {expected}");
        }

        /// <summary>
        /// Emits one JSONL stage timeline per scenario. Consumed by
        /// <c>tools/vrf_exposure_timeline_compare.py</c> as the VRF candidate side.
        /// </summary>
        [Test]
        public async Task EmitStageTimelines()
        {
            var (scenarios, corpusSha) = LoadScenarios();

            var outDir = Environment.GetEnvironmentVariable("VRF_EXPOSURE_TIMELINE_OUT")
                ?? Path.Combine(TestContext.TestDirectory!, "exposure_timelines");
            Directory.CreateDirectory(outDir);

            var vrfCommit = Environment.GetEnvironmentVariable("VRF_EXPOSURE_VRF_COMMIT") ?? "unknown";

            foreach (var scenario in scenarios)
            {
                var records = RunMirror(scenario);
                var path = Path.Combine(outDir, $"{scenario.Name}.vrf.jsonl");

                var stream = File.Create(path);
                await using (stream.ConfigureAwait(false))
                {
                    var writer = new StreamWriter(stream, new UTF8Encoding(false));
                    await using (writer.ConfigureAwait(false))
                    {
                        for (var i = 0; i < records.Count; i++)
                        {
                            var settings = scenario.Frames[i].Settings ?? scenario.Settings;
                            await writer.WriteLineAsync(
                                SerializeFrame(records[i], scenario, settings, corpusSha, vrfCommit));
                        }
                    }
                }
            }

            Console.WriteLine($"exposure timelines written to {outDir}");
            await Assert.That(Directory.GetFiles(outDir, "*.vrf.jsonl").Length).IsEqualTo(scenarios.Count);
        }

        private static string Num(float value)
        {
            if (float.IsNaN(value))
            {
                return "\"NaN\"";
            }

            if (float.IsPositiveInfinity(value))
            {
                return "\"Infinity\"";
            }

            if (float.IsNegativeInfinity(value))
            {
                return "\"-Infinity\"";
            }

            return value.ToString("R", CultureInfo.InvariantCulture);
        }

        private static string Bool(bool value) => value ? "true" : "false";

        private static string SerializeFrame(ExposureFrame f, Scenario scenario, ExposureSettings settings, string corpusSha, string vrfCommit)
        {
            var history = new StringBuilder("[");
            for (var i = 0; i < f.History.Length; i++)
            {
                if (i > 0)
                {
                    history.Append(',');
                }

                history.Append(Num(f.History[i]));
            }

            history.Append(']');

            var exposureBiasScale = MathF.Pow(2.0f, scenario.ExposureBias);

            var sb = new StringBuilder(1024);
            sb.Append("{\"schema\":\"").Append(TimelineSchema).Append('"');
            sb.Append(",\"producer\":\"vrf_csharp\"");
            sb.Append(",\"vrf_commit\":\"").Append(vrfCommit).Append('"');
            sb.Append(",\"scenario\":\"").Append(scenario.Name).Append('"');
            sb.Append(",\"scenario_corpus_sha256\":\"").Append(corpusSha).Append('"');
            sb.Append(",\"frame\":").Append(f.Frame.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"delta_time\":").Append(Num(f.DeltaTime));
            sb.Append(",\"settings\":{")
              .Append("\"autoExposureEnabled\":").Append(Bool(settings.AutoExposureEnabled))
              .Append(",\"exposureMin\":").Append(Num(settings.ExposureMin))
              .Append(",\"exposureMax\":").Append(Num(settings.ExposureMax))
              .Append(",\"exposureSpeedUp\":").Append(Num(settings.ExposureSpeedUp))
              .Append(",\"exposureSpeedDown\":").Append(Num(settings.ExposureSpeedDown))
              .Append(",\"exposureSmoothingRange\":").Append(Num(settings.ExposureSmoothingRange))
              .Append(",\"exposureCompensation\":").Append(Num(settings.ExposureCompensation))
              .Append('}');

            sb.Append(",\"stages\":{");
            sb.Append("\"average_luminance\":{\"value\":").Append(Num(f.AverageLuminance)).Append('}');
            sb.Append(",\"raw_scalar\":{\"value\":").Append(Num(f.RawScalar))
              .Append(",\"finite\":").Append(Bool(f.RawScalarFinite)).Append('}');
            sb.Append(",\"clamp\":{\"value\":").Append(Num(f.ClampedScalar))
              .Append(",\"min\":").Append(Num(settings.ExposureMin))
              .Append(",\"max\":").Append(Num(settings.ExposureMax)).Append('}');
            sb.Append(",\"history\":{\"count\":").Append(f.History.Length.ToString(CultureInfo.InvariantCulture))
              .Append(",\"values\":").Append(history)
              .Append(",\"window_used\":").Append(Bool(f.HistoryWindowUsed))
              .Append(",\"weighted_sum\":").Append(Num(f.WeightedSum))
              .Append(",\"weight_total\":").Append(Num(f.WeightTotal)).Append('}');
            sb.Append(",\"target\":{\"value\":").Append(Num(f.TargetExposure)).Append('}');
            sb.Append(",\"speed\":{\"branch\":\"").Append(f.SpeedBranch).Append('"')
              .Append(",\"adapt_rate_selected\":").Append(Num(f.AdaptRateSelected))
              .Append(",\"smoothing_applied\":").Append(Bool(f.SmoothingApplied)).Append('}');
            sb.Append(",\"log2_dt\":{\"adapt_rate_scaled\":").Append(Num(f.AdaptRateScaled))
              .Append(",\"overshoot_clamped\":").Append(Bool(f.OvershootClamped))
              .Append(",\"value\":").Append(Num(f.CurrentExposure)).Append('}');
            sb.Append(",\"compensation\":{\"scale\":").Append(Num(f.CompensationScale))
              .Append(",\"value\":").Append(Num(f.TonemapScalar)).Append('}');
            sb.Append(",\"application\":{\"tonemap_scalar\":").Append(Num(f.TonemapScalar))
              .Append(",\"exposure_bias_scale\":").Append(Num(exposureBiasScale))
              .Append(",\"combined\":").Append(Num(f.TonemapScalar * exposureBiasScale)).Append('}');
            sb.Append("}}");

            return sb.ToString();
        }

        // ------------------------------------------------------------------
        // Characterization of current behavior. These are descriptive, not normative.
        // ------------------------------------------------------------------

        private static ExposureSettings Auto(float min = 0.25f, float max = 8.0f, float up = 1.0f, float down = 1.0f,
            float smoothing = 100f, float compensation = 0.0f)
        {
            var settings = new ExposureSettings
            {
                ExposureMin = min,
                ExposureMax = max,
                ExposureSpeedUp = up,
                ExposureSpeedDown = down,
                ExposureSmoothingRange = smoothing,
                ExposureCompensation = compensation,
            };
            settings.AutoExposureEnabled = true;
            return settings;
        }

        private static PostProcessRenderer NewRenderer(ExposureSettings settings)
        {
            return new PostProcessRenderer(null!)
            {
                State = PostProcessState.Default with { ExposureSettings = settings },
            };
        }

        [Test]
        public async Task HistoryWeightsAreVShapedAndSumToFive()
        {
            var weights = new float[10];
            var total = 0.0f;

            for (var i = 0; i < 10; i++)
            {
                weights[i] = Math.Abs(5 - i) * 0.2f;
                total += weights[i];
            }

            // The window is an inverted triangle: the oldest sample carries the most weight,
            // the middle sample carries none, and the newest carries 0.8.
            await Assert.That(weights[0]).IsEqualTo(1.0f).Within(1e-6f).Because("oldest sample weight");
            await Assert.That(weights[5]).IsEqualTo(0.0f).Within(1e-6f).Because("middle sample weight");
            await Assert.That(weights[9]).IsEqualTo(0.8f).Within(1e-6f).Because("newest sample weight");
            await Assert.That(total).IsEqualTo(5.0f).Within(1e-6f).Because("total weight");
            await Assert.That(weights[0]).IsGreaterThan(weights[9]).Because("oldest outweighs newest");
        
        }

        [Test]
        public async Task FirstNineFramesBypassTheHistoryWindow()
        {
            // ExposureMax is raised past the tested range so the clamp does not
            // confound the window switch this test is about.
            var renderer = NewRenderer(Auto(max: 100.0f, up: 0.0f));

            for (var frame = 1; frame <= 9; frame++)
            {
                renderer.AverageLuminance = 0.18f / frame;
                renderer.CalculateTonemapScalar(1.0f / 60.0f);

                await Assert.That(renderer.ExposureHistory.Count).IsEqualTo(frame);
                // speed-up 0 snaps to target, so TonemapScalar is the clamped raw scalar itself
                await Assert.That(renderer.TargetExposure).IsEqualTo((float)frame).Within(1e-5f).Because($"frame {frame}");
            }

            // Frame 10 switches to the weighted window and the value changes even though the
            // newest sample is unchanged from a pure-raw perspective.
            renderer.AverageLuminance = 0.18f / 10f;
            renderer.CalculateTonemapScalar(1.0f / 60.0f);

            await Assert.That(renderer.ExposureHistory.Count).IsEqualTo(10);

            var expectedWeighted = 0.0f;
            for (var i = 0; i < 10; i++)
            {
                expectedWeighted += Math.Abs(5 - i) * 0.2f * (i + 1);
            }

            expectedWeighted /= 5.0f;
            await Assert.That(renderer.TargetExposure).IsEqualTo(expectedWeighted).Within(1e-4f);
            await Assert.That(renderer.TargetExposure).IsLessThan(10f).Because("the window lags well behind the newest sample");
        }

        [Test]
        public async Task HistoryEvictsOldestAndNeverExceedsTen()
        {
            var renderer = NewRenderer(Auto(max: 100.0f, up: 0.0f));

            for (var frame = 0; frame < 25; frame++)
            {
                renderer.AverageLuminance = 0.18f / (frame + 1);
                renderer.CalculateTonemapScalar(1.0f / 60.0f);
                await Assert.That(renderer.ExposureHistory.Count).IsLessThanOrEqualTo(10);
            }

            await Assert.That(renderer.ExposureHistory.Count).IsEqualTo(10);
            await Assert.That(renderer.ExposureHistory[9]).IsEqualTo(25f).Within(1e-4f).Because("newest sample is last");
            await Assert.That(renderer.ExposureHistory[0]).IsEqualTo(16f).Within(1e-4f).Because("oldest retained sample");
        }

        [Test]
        public async Task ClampBoundsTheTargetInBothDirections()
        {
            var renderer = NewRenderer(Auto(min: 0.5f, max: 2.0f, up: 0.0f));

            renderer.AverageLuminance = 0.18f / 100f; // raw scalar 100
            renderer.CalculateTonemapScalar(1.0f / 60.0f);
            await Assert.That(renderer.TargetExposure).IsEqualTo(2.0f);

            renderer.AverageLuminance = 0.18f / 0.001f; // raw scalar 0.001
            renderer.CalculateTonemapScalar(1.0f / 60.0f);
            await Assert.That(renderer.TargetExposure).IsEqualTo(0.5f);
        }

        [Test]
        public async Task SpeedUpZeroSnapsRegardlessOfDirection()
        {
            var renderer = NewRenderer(Auto(up: 0.0f, down: 5.0f));

            renderer.AverageLuminance = 0.18f / 4f;
            renderer.CalculateTonemapScalar(1.0f / 60.0f);
            await Assert.That(renderer.CurrentExposure).IsEqualTo(4.0f).Within(1e-5f).Because("snap up");

            renderer.AverageLuminance = 0.18f / 0.5f;
            renderer.CalculateTonemapScalar(1.0f / 60.0f);
            await Assert.That(renderer.CurrentExposure).IsEqualTo(0.5f).Within(1e-5f).Because("snap down");
        }

        [Test]
        public async Task AdaptationIsDirectionallyDistinctForAsymmetricSpeeds()
        {
            const float dt = 1.0f / 60.0f;

            var up = NewRenderer(Auto(up: 4.0f, down: 0.25f, smoothing: 0.0f));
            up.AverageLuminance = 0.18f / 4f;
            up.CalculateTonemapScalar(dt);

            var down = NewRenderer(Auto(up: 4.0f, down: 0.25f, smoothing: 0.0f));
            down.AverageLuminance = 0.18f / 0.25f;
            down.CalculateTonemapScalar(dt);

            var upStep = MathF.Log2(up.CurrentExposure) - 0.0f;
            var downStep = 0.0f - MathF.Log2(down.CurrentExposure);

            await Assert.That(upStep).IsEqualTo(4.0f * dt).Within(1e-5f).Because("up step is speedUp * dt in log2 space");
            await Assert.That(downStep).IsEqualTo(0.25f * dt).Within(1e-5f).Because("down step is speedDown * dt in log2 space");
        
        }

        [Test]
        public async Task SmoothingRangeDampsSmallLogDifferences()
        {
            const float dt = 1.0f;

            // logDiff = 1 stop, smoothing range 100 -> rate becomes min(0.5, speed)
            var renderer = NewRenderer(Auto(up: 4.0f, smoothing: 100f));
            renderer.AverageLuminance = 0.18f / 2f;
            renderer.CalculateTonemapScalar(dt);

            await Assert.That(MathF.Log2(renderer.CurrentExposure)).IsEqualTo(0.5f).Within(1e-5f).Because("damped rate is half the log2 distance, not the configured speed");
        }

        [Test]
        public async Task AdaptationNeverOvershootsTheTarget()
        {
            var renderer = NewRenderer(Auto(up: 100f, down: 100f, smoothing: 0.0f));

            renderer.AverageLuminance = 0.18f / 4f;
            renderer.CalculateTonemapScalar(1.0f);
            await Assert.That(renderer.CurrentExposure).IsEqualTo(4.0f).Because("clamped exactly onto the target going up");

            renderer.AverageLuminance = 0.18f / 0.5f;
            renderer.CalculateTonemapScalar(1.0f);
            await Assert.That(renderer.CurrentExposure).IsEqualTo(0.5f).Because("clamped exactly onto the target going down");
        }

        [Test]
        public async Task ZeroDeltaTimeHoldsInBothDirections()
        {
            // The overshoot guard decides its direction from the pre-deltaTime
            // adaptation rate, so a zero-length frame holds in BOTH directions
            // instead of collapsing downward through -0.0f >= 0.
            var upward = NewRenderer(Auto(up: 1.0f, down: 1.0f));
            upward.AverageLuminance = 0.18f / 4f;
            upward.CalculateTonemapScalar(0.0f);
            await Assert.That(upward.CurrentExposure).IsEqualTo(1.0f).Because("upward holds across a zero-length frame");

            var downward = NewRenderer(Auto(up: 1.0f, down: 1.0f));
            downward.AverageLuminance = 0.18f / 0.25f;
            downward.CalculateTonemapScalar(0.0f);
            await Assert.That(downward.CurrentExposure).IsEqualTo(1.0f).Because("downward holds across a zero-length frame too");
        }

        [Test]
        public async Task SpeedDownZeroSnapsThroughTheSameNegativeZeroPath()
        {
            var renderer = NewRenderer(Auto(up: 1.0f, down: 0.0f));

            renderer.AverageLuminance = 0.18f / 0.25f;
            renderer.CalculateTonemapScalar(1.0f / 60.0f);

            await Assert.That(renderer.CurrentExposure).IsEqualTo(0.25f).Because("a zero downward speed snaps instantly rather than freezing");
        }

        [Test]
        public async Task NonFiniteRawScalarReturnsOneInsteadOfHoldingCurrentExposure()
        {
            var renderer = NewRenderer(Auto(up: 0.0f));

            renderer.AverageLuminance = 0.18f / 4f;
            renderer.CalculateTonemapScalar(1.0f / 60.0f);
            await Assert.That(renderer.TonemapScalar).IsEqualTo(4.0f).Within(1e-5f);

            // A zero average luminance makes the raw scalar infinite.
            renderer.AverageLuminance = 0.0f;
            renderer.CalculateTonemapScalar(1.0f / 60.0f);

            await Assert.That(renderer.TonemapScalar).IsEqualTo(1.0f).Because("the applied scalar drops to 1.0 for this frame");
            await Assert.That(renderer.CurrentExposure).IsEqualTo(4.0f).Within(1e-5f).Because("the smoothed state is left untouched");
            await Assert.That(renderer.ExposureHistory.Count).IsEqualTo(1).Because("the bad sample is not pushed into history");
        
        }

        [Test]
        public async Task CompensationScalesTheAppliedScalarWithoutTouchingAdaptation()
        {
            var plain = NewRenderer(Auto(up: 0.0f, compensation: 0.0f));
            plain.AverageLuminance = 0.18f / 2f;
            plain.CalculateTonemapScalar(1.0f / 60.0f);

            var compensated = NewRenderer(Auto(up: 0.0f, compensation: 1.0f));
            compensated.AverageLuminance = 0.18f / 2f;
            compensated.CalculateTonemapScalar(1.0f / 60.0f);

            await Assert.That(compensated.CurrentExposure).IsEqualTo(plain.CurrentExposure).Because("adaptation state is unaffected");
            await Assert.That(compensated.TonemapScalar).IsEqualTo(plain.TonemapScalar * 2.0f).Within(1e-5f).Because("one stop applied");
        
        }

        [Test]
        public async Task AutoExposureDisabledIgnoresLuminanceAndAppliesCompensationOnly()
        {
            var settings = Auto(compensation: -1.0f);
            settings.AutoExposureEnabled = false;

            var renderer = NewRenderer(settings);
            renderer.AverageLuminance = 0.18f / 6f;
            renderer.CalculateTonemapScalar(1.0f / 60.0f);

            await Assert.That(renderer.TonemapScalar).IsEqualTo(0.5f).Because("1.0 scaled by 2^-1");
            await Assert.That(renderer.ExposureHistory).IsEmpty();
        
        }

        [Test]
        public async Task CustomExposureBypassesTheWholeChainAndDisablesAutoExposureInState()
        {
            var renderer = NewRenderer(Auto());
            renderer.CustomExposure = 0.75f;
            renderer.AverageLuminance = 0.18f / 6f;
            renderer.CalculateTonemapScalar(1.0f / 60.0f);

            await Assert.That(renderer.TonemapScalar).IsEqualTo(0.75f).Because("compensation is not applied on this path");
            await Assert.That(renderer.State.ExposureSettings.AutoExposureEnabled).IsFalse().Because("the state flag is cleared so the histogram pass is skipped this frame");
            await Assert.That(renderer.ExposureHistory).IsEmpty();
        
        }

        [Test]
        public async Task VariableDeltaTimeIntegratesInLog2Space()
        {
            // Two half-length frames must land on the same exposure as one full-length frame,
            // because the integration is linear in log2 space when neither frame clamps.
            var single = NewRenderer(Auto(up: 1.0f, smoothing: 0.0f));
            single.AverageLuminance = 0.18f / 8f;
            single.CalculateTonemapScalar(0.5f);

            var split = NewRenderer(Auto(up: 1.0f, smoothing: 0.0f));
            split.AverageLuminance = 0.18f / 8f;
            split.CalculateTonemapScalar(0.25f);
            split.CalculateTonemapScalar(0.25f);

            await Assert.That(split.CurrentExposure).IsEqualTo(single.CurrentExposure).Within(1e-5f);
        }

        [Test]
        public async Task ExposureIsCalculatedFromAPreviousFramesLuminance()
        {
            // Renderer.cs assigns State and calls CalculateTonemapScalar in the pre-render
            // update, while ComputeAverageLuminance runs later in the same frame. The value
            // consumed here is therefore always produced by an earlier frame. This test pins
            // the ownership assumption that the timeline schema encodes.
            var renderer = NewRenderer(Auto(up: 0.0f));

            renderer.AverageLuminance = 0.18f / 2f;
            renderer.CalculateTonemapScalar(1.0f / 60.0f);
            var afterFirst = renderer.TonemapScalar;

            // A new luminance arriving after the calculation cannot affect this frame.
            renderer.AverageLuminance = 0.18f / 8f;
            await Assert.That(renderer.TonemapScalar).IsEqualTo(afterFirst);
        }
    }
}
