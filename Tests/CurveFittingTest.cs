using System.Threading.Tasks;
using ValveResourceFormat.Particles.Utils;
using static ValveResourceFormat.Particles.Utils.CurvePoint;

namespace Tests
{
    public class CurveFittingTest
    {
        [Test]
        public async Task SplineTangentsCurveThroughTheMidpoint()
        {
            var points = new[]
            {
                Point(0f, 0f, TangentType.Spline, TangentType.Spline),
                Point(1f, 1f, TangentType.Spline, TangentType.Spline),
                Point(2f, 0f, TangentType.Spline, TangentType.Spline),
            };

            PiecewiseCurve.ResolveSlopes(points);
            var segment = CurveFitting.GetCoefficients(points[0], points[1]);

            await Assert.That(segment.Evaluate(0.5f)).IsEqualTo(0.625f);
        }

        [Test]
        public async Task FreeTangentsKeepTheirAuthoredSlopes()
        {
            var points = new[]
            {
                Point(0f, 0f, TangentType.Free, TangentType.Free, slopeOutgoing: 2f),
                Point(1f, 1f, TangentType.Free, TangentType.Free),
            };

            PiecewiseCurve.ResolveSlopes(points);
            var segment = CurveFitting.GetCoefficients(points[0], points[1]);

            await Assert.That(segment.Evaluate(0.5f)).IsEqualTo(0.75f);
        }

        [Test]
        public async Task MirrorTangentTakesTheOtherSideSlope()
        {
            var points = new[]
            {
                Point(0f, 0f, TangentType.Mirror, TangentType.Free, slopeOutgoing: 3f),
            };

            PiecewiseCurve.ResolveSlopes(points);

            await Assert.That(points[0].SlopeIncoming).IsEqualTo(3f);
            await Assert.That(points[0].SlopeOutgoing).IsEqualTo(3f);
        }

        [Test]
        public async Task SineTangentScalesItsSlopeByTheNeighbourSpan()
        {
            var points = new[]
            {
                Point(0f, 0f, TangentType.Spline, TangentType.Spline),
                Point(1f, 1f, TangentType.Sine, TangentType.Spline),
                Point(2f, 2f, TangentType.Spline, TangentType.Spline),
            };

            PiecewiseCurve.ResolveSlopes(points);

            await Assert.That(points[1].SlopeIncoming).IsEqualTo(-0.041337699f);
        }

        [Test]
        public async Task RepeatedPositionsArePushedApartBeforeSlopesResolve()
        {
            var points = new[]
            {
                Point(0f, 0f, TangentType.Linear, TangentType.Linear),
                Point(0f, 1f, TangentType.Linear, TangentType.Linear),
            };

            PiecewiseCurve.ResolveSlopes(points);

            await Assert.That(points[1].X).IsGreaterThan(0f);
            await Assert.That(points[1].SlopeIncoming).IsEqualTo(10000f).Within(1f);
        }

        private static CurvePoint Point(float x, float y, TangentType incoming, TangentType outgoing, float slopeIncoming = 0f, float slopeOutgoing = 0f)
        {
            return new CurvePoint
            {
                X = x,
                Y = y,
                IncomingTangent = incoming,
                OutgoingTangent = outgoing,
                SlopeIncoming = slopeIncoming,
                SlopeOutgoing = slopeOutgoing,
            };
        }
    }
}
