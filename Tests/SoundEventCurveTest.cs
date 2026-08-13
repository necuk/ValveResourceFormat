using System.IO;
using System.Text;
using NUnit.Framework;
using ValveResourceFormat.Renderer.Audio;
using ValveResourceFormat.Serialization.KeyValues;

namespace Tests
{
    [TestFixture]
    public class SoundEventCurveTest
    {
        private static SoundEventCurve ParseCurve(string points)
        {
            var text = "<!-- kv3 encoding:text:version{e21c7f3c-8a33-41c5-9977-a76d3a32aa0d} format:generic:version{7412167c-06e9-4698-aff2-e63eb59037e7} -->\n{\n\tcurve = " + points + "\n}\n";
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(text));
            var curve = SoundEventCurve.Parse(KVDocumentExtensions.ParseKV3(stream).Root, "curve");
            Assert.That(curve, Is.Not.Null);
            return curve;
        }

        [Test]
        public void LinearTangentsEvaluateLinearly()
        {
            var curve = ParseCurve("[[0.0, 1.0, 0.0, 0.0, 0.0, 0.0], [100.0, 0.0, 0.0, 0.0, 0.0, 0.0]]");

            Assert.That(curve.Evaluate(0f), Is.EqualTo(1f));
            Assert.That(curve.Evaluate(25f), Is.EqualTo(0.75f));
            Assert.That(curve.Evaluate(50f), Is.EqualTo(0.5f));
            Assert.That(curve.Evaluate(100f), Is.EqualTo(0f));
        }

        [Test]
        public void SplineTangentsBendTheSpan()
        {
            var curve = ParseCurve("[[0.0, 0.0, 0.0, 0.0, 1.0, 1.0], [50.0, 1.0, 0.0, 0.0, 1.0, 1.0], [100.0, 0.0, 0.0, 0.0, 1.0, 1.0]]");

            Assert.That(curve.Evaluate(25f), Is.EqualTo(0.625f), "A linear read gives 0.5");
            Assert.That(curve.Evaluate(75f), Is.EqualTo(0.625f), "A linear read gives 0.5");
            Assert.That(curve.Evaluate(50f), Is.EqualTo(1f));
        }

        [Test]
        public void FreeTangentsUseTheAuthoredValues()
        {
            var curve = ParseCurve("[[0.0, 1.0, 0.0, -0.02, 2.0, 2.0], [100.0, 0.0, -0.02, 0.0, 2.0, 2.0]]");

            Assert.That(curve.Evaluate(25f), Is.EqualTo(0.65625f), "A linear read gives 0.75");
        }

        [Test]
        public void MirrorTangentCopiesTheOppositeHandle()
        {
            var curve = ParseCurve("[[0.0, 0.0, 0.0, 0.0, 0.0, 0.0], [50.0, 1.0, 0.0, -0.02, 3.0, 2.0], [100.0, 0.0, 0.0, 0.0, 0.0, 0.0]]");

            Assert.That(curve.Evaluate(25f), Is.EqualTo(0.75f), "A linear read gives 0.5");
        }

        [Test]
        public void SineTangentsUseFixedSlopes()
        {
            var curve = ParseCurve("[[0.0, 1.0, 0.0, 0.0, 4.0, 4.0], [100.0, 0.0, 0.0, 0.0, 4.0, 4.0]]");

            Assert.That(curve.Evaluate(25f), Is.EqualTo(0.45620906f).Within(0.000001f), "A linear read gives 0.75");
            Assert.That(curve.Evaluate(75f), Is.EqualTo(0.54379094f).Within(0.000001f), "A linear read gives 0.25");
        }

        [Test]
        public void FootstepDistanceCurveMatchesTheGameShape()
        {
            var curve = ParseCurve(
                "[[49.591427, 0.45, 0.010452, 0.010452, 3.0, 1.0], " +
                "[116.563492, 1.0, -0.001429, -0.001429, 2.0, 3.0], " +
                "[402.285736, 0.488971, -0.000991, -0.000991, 3.0, 1.0], " +
                "[1095.0, 0.03, -0.000701, -0.000701, 1.0, 1.0], " +
                "[1100.0, 0.0, -0.000476, -0.000476, 2.0, 3.0]]");

            Assert.That(curve.Evaluate(300f), Is.EqualTo(0.64675820f).Within(0.000001f), "A linear read gives 0.6719");
        }

        [Test]
        public void GrenadeExplosionDistanceCurveMatchesTheGameShape()
        {
            var curve = ParseCurve(
                "[[0.0, 1.0, 0.0, 0.0, 0.0, 0.0], " +
                "[231.428574, 1.0, -0.000553, -0.000553, 1.0, 1.0], " +
                "[779.142883, 0.569231, -0.000405, -0.000405, 1.0, 1.0], " +
                "[2700.0, 0.0, -0.000096, -0.000096, 2.0, 3.0]]");

            Assert.That(curve.Evaluate(1800f), Is.EqualTo(0.19140819f).Within(0.000001f), "A linear read gives 0.2667");
        }

        [Test]
        public void EvaluateClampsToTheEndPoints()
        {
            var curve = ParseCurve("[[0.0, 1.0, 0.0, 0.0, 0.0, 0.0], [100.0, 0.0, 0.0, 0.0, 0.0, 0.0]]");

            Assert.That(curve.Evaluate(-10f), Is.EqualTo(1f));
            Assert.That(curve.Evaluate(200f), Is.EqualTo(0f));
        }

        [Test]
        public void EvaluateClampsOvershootToTheAuthoredExtent()
        {
            var curve = ParseCurve("[[0.0, 0.0, 0.0, 5.0, 2.0, 2.0], [10.0, 1.0, -5.0, 0.0, 2.0, 2.0]]");

            Assert.That(curve.Evaluate(5f), Is.EqualTo(1f));
        }

        [Test]
        public void SingleKnotEvaluatesToItsValue()
        {
            var curve = ParseCurve("[[50.0, 0.75, 0.0, 0.0, 0.0, 0.0]]");

            Assert.That(curve.Evaluate(0f), Is.EqualTo(0.75f));
            Assert.That(curve.Evaluate(50f), Is.EqualTo(0.75f));
            Assert.That(curve.Evaluate(100f), Is.EqualTo(0.75f));
        }
    }
}
