using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using OpenTK.Graphics.OpenGL;
using ValveResourceFormat.CompiledShader;
using ValveResourceFormat.IO;
using ValveResourceFormat.Renderer;
using ValveResourceFormat.Renderer.PostProcess;
using ValveResourceFormat.Renderer.SceneEnvironment;

namespace Tests.Renderer;

public class TonemapSettingsTest
{
    [Test]
    public async Task WhitePointIsConvertedFromStopsToLinear()
    {
        var cases = new[]
        {
            (Map: "de_dust2", Authored: 1.648926f, Engine: 3.1360011100769043f),
            (Map: "de_inferno", Authored: 4.0f, Engine: 16.0f),
        };

        foreach (var testCase in cases)
        {
            var settings = new TonemapSettings { WhitePoint = testCase.Authored };

            await Assert.That(settings.EffectiveWhitePoint)
                .IsEqualTo(testCase.Engine)
                .Within(1e-5f)
                .Because(testCase.Map);
        }
    }

    [Test]
    public async Task AutoExposureTargetsEngineMiddleGrey()
    {
        using var fileLoader = new GameFileLoader(null, null);
        using var context = new RendererContext(fileLoader, NullLogger.Instance);
        var postProcess = new PostProcessRenderer(context)
        {
            AverageLuminance = 0.225f,
            State = PostProcessState.Default with
            {
                TonemapSettings = new TonemapSettings { WhitePoint = 4.0f },
                ExposureSettings = new ExposureSettings
                {
                    AutoExposureEnabled = true,
                    ExposureSpeedUp = 0.0f,
                },
            },
        };

        postProcess.CalculateTonemapScalar(1.0f / 60.0f);

        await Assert.That(postProcess.TargetExposure).IsEqualTo(0.8f).Within(1e-6f);
        await Assert.That(postProcess.TonemapScalar).IsEqualTo(0.8f).Within(1e-6f);
    }

    [Test]
    public async Task AutoExposureUsesEngineDefaults()
    {
        var settings = new ExposureSettings();

        await Assert.That(settings.AutoExposureEnabled).IsTrue();
        await Assert.That(settings.ExposureMin).IsEqualTo(0.25f);
        await Assert.That(settings.ExposureMax).IsEqualTo(8.0f);
        await Assert.That(settings.ExposureSpeedUp).IsEqualTo(1.0f);
        await Assert.That(settings.ExposureSpeedDown).IsEqualTo(2.0f);
        await Assert.That(settings.ExposureSmoothingRange).IsEqualTo(100.0f);
        await Assert.That(settings.ExposureCompensation).IsEqualTo(0.0f);
    }

    [Test]
    public async Task EngineSceneColorFormatHasOpenGlMapping()
    {
        await Assert.That(ImageFormat.IMAGE_FORMAT_R11G11B10_FLOAT.ToGLSizedInternalFormat())
            .IsEqualTo(SizedInternalFormat.R11fG11fB10f);
        await Assert.That(ImageFormat.IMAGE_FORMAT_R11G11B10_FLOAT.ToGLPixelFormat())
            .IsEqualTo(PixelFormat.Rgb);
        await Assert.That(ImageFormat.IMAGE_FORMAT_R11G11B10_FLOAT.ToGLPixelType())
            .IsEqualTo(PixelType.UnsignedInt10F11F11FRev);
    }
}
