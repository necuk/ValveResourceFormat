using NUnit.Framework;
using ValveResourceFormat.Renderer.Audio;

namespace Tests;

[TestFixture]
public sealed class SoundEventCurveTests
{
    [Test]
    public void LinearCurve_EvaluatesLinearly()
    {
        var curve = SoundEventCurve.CreateTestCurve(
            [(0f, 1f), (100f, 0f)],
            [(0, 0), (0, 0)]
        );

        Assert.That(curve.Evaluate(0f), Is.EqualTo(1f).Within(0.0001f), "Start point");
        Assert.That(curve.Evaluate(50f), Is.EqualTo(0.5f).Within(0.0001f), "Midpoint");
        Assert.That(curve.Evaluate(100f), Is.EqualTo(0f).Within(0.0001f), "End point");
    }

    [Test]
    public void SplineCurve_UsesSpanSlope()
    {
        var curve = SoundEventCurve.CreateTestCurve(
            [(0f, 0f), (50f, 1f), (100f, 0f)],
            [(1, 1), (1, 1), (1, 1)]
        );

        Assert.That(curve.Evaluate(0f), Is.EqualTo(0f).Within(0.0001f), "Start point");
        Assert.That(curve.Evaluate(100f), Is.EqualTo(0f).Within(0.0001f), "End point");
        Assert.That(curve.Evaluate(50f), Is.EqualTo(1f).Within(0.0001f), "Peak");
    }

    [Test]
    public void Evaluate_ClampsToYBounds()
    {
        var curve = SoundEventCurve.CreateTestCurve(
            [(0f, 1f), (100f, 0f)],
            [(0, 0), (0, 0)]
        );

        Assert.That(curve.Evaluate(-10f), Is.EqualTo(1f).Within(0.0001f), "Below min");
        Assert.That(curve.Evaluate(200f), Is.EqualTo(0f).Within(0.0001f), "Above max");
    }

    [Test]
    public void SingleKnot_EvaluatesToItsYValue()
    {
        var curve = SoundEventCurve.CreateTestCurve(
            [(50f, 0.75f)],
            [(0, 0)]
        );

        Assert.That(curve.Evaluate(0f), Is.EqualTo(0.75f).Within(0.0001f));
        Assert.That(curve.Evaluate(50f), Is.EqualTo(0.75f).Within(0.0001f));
        Assert.That(curve.Evaluate(100f), Is.EqualTo(0.75f).Within(0.0001f));
    }

    [Test]
    public void MixedTangentTypes_ResolvesCorrectly()
    {
        var curve = SoundEventCurve.CreateTestCurve(
            [(0f, 0f), (50f, 1f), (100f, 0f)],
            [(0, 1), (1, 0), (0, 0)]
        );

        Assert.That(curve.Evaluate(25f), Is.GreaterThan(0f).And.LessThan(1f));
        Assert.That(curve.Evaluate(75f), Is.GreaterThan(0f).And.LessThan(1f));
    }
}
