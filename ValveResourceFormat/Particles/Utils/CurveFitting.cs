using ValveKeyValue;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Particles.Utils
{
    class SplineCurve
    {
        public float a { get; set; }
        public float b { get; set; }
        public float c { get; set; }
        public float d { get; set; }
        public Vector2 Start { get; set; }
        public Vector2 End { get; set; }

        public float Evaluate(float x)
        {
            // Coefficients are relative to the segment start
            var t = (x - Start.X) / (End.X - Start.X);
            return a + t * (b + t * (c + t * d));
        }

        public bool IsInCurve(float x)
        {
            return x >= Start.X && x <= End.X;
        }
    }
    internal static class CurveFitting
    {
        public static SplineCurve GetCoefficients(CurvePoint p0, CurvePoint p1)
        {
            var dx = p1.X - p0.X;
            var dy = p1.Y - p0.Y;
            var slope0 = p0.SlopeOutgoing;
            var slope1 = p1.SlopeIncoming;

            return new SplineCurve
            {
                Start = p0.Pos,
                End = p1.Pos,
                a = p0.Y,
                b = dx * slope0,
                c = ((-slope1 - (slope0 + slope0)) * dx) + (dy * 3f),
                d = ((slope1 + slope0) * dx) - (dy + dy),
            };
        }
    }
    internal class CurvePoint
    {
        /// <summary>
        /// Tangent interpolation modes for curve control points.
        /// </summary>
        public enum TangentType
        {
            /// <summary>Linear interpolation between adjacent control points.</summary>
            Linear,
            /// <summary>Cubic spline interpolation.</summary>
            Spline,
            /// <summary>Linear tangent with independent incoming and outgoing slopes.</summary>
            Free,
            /// <summary>Tangent mirrors its opposite handle.</summary>
            Mirror,
            /// <summary>Sine-wave tangent interpolation.</summary>
            Sine
        };
        public static TangentType GetTangentType(string value)
        {
            return value switch
            {
                "CURVE_TANGENT_LINEAR" => TangentType.Linear,
                "CURVE_TANGENT_SPLINE" => TangentType.Spline,
                "CURVE_TANGENT_FREE" => TangentType.Free,
                "CURVE_TANGENT_MIRROR" => TangentType.Mirror,
                "CURVE_TANGENT_SINE" => TangentType.Sine,
                // Unknown tangent modes interpolate linearly.
                _ => TangentType.Linear,
            };
        }

        public float SlopeIncoming { get; set; }
        public float SlopeOutgoing { get; set; }
        public float X { get; set; }
        public float Y { get; set; }
        public TangentType IncomingTangent { get; set; }
        public TangentType OutgoingTangent { get; set; }
        public Vector2 Pos => new(X, Y);
    }

    /// <summary>
    /// A piecewise curve used in particle systems' dynamic parameters.
    /// </summary>
    class PiecewiseCurve
    {
        private readonly Vector2 curveDomainMin;
        private readonly Vector2 curveDomainMax;
        private readonly SplineCurve[] curveSegments;
        private readonly bool isLooped;
        public PiecewiseCurve(KVObject curveInfo, bool isLooped)
        {
            this.isLooped = isLooped;

            var domainMin = curveInfo.GetFloatArray("m_vDomainMins");
            var domainMax = curveInfo.GetFloatArray("m_vDomainMaxs");

            curveDomainMin = new Vector2(domainMin[0], domainMin[1]);
            curveDomainMax = new Vector2(domainMax[0], domainMax[1]);

            // Gather curve points. The engine truncates both arrays to the shorter count when their
            // lengths differ (m_tangents can be missing or short in old content).
            var splines = curveInfo.GetArray("m_spline");
            var tangents = curveInfo.ContainsKey("m_tangents") ? curveInfo.GetArray("m_tangents") : [];
            var pointCount = Math.Min(splines.Count, tangents.Count);

            var CurvePoints = new CurvePoint[pointCount];

            for (var i = 0; i < pointCount; i++)
            {
                CurvePoints[i] = new CurvePoint
                {
                    X = splines[i].GetFloatProperty("x"),
                    Y = splines[i].GetFloatProperty("y"),
                    IncomingTangent = CurvePoint.GetTangentType(tangents[i].GetStringProperty("m_nIncomingTangent")),
                    OutgoingTangent = CurvePoint.GetTangentType(tangents[i].GetStringProperty("m_nOutgoingTangent")),
                    SlopeIncoming = splines[i].GetFloatProperty("m_flSlopeIncoming"),
                    SlopeOutgoing = splines[i].GetFloatProperty("m_flSlopeOutgoing"),
                };
            }

            ResolveSlopes(CurvePoints);

            curveSegments = new SplineCurve[Math.Max(0, pointCount - 1)];

            for (var i = 0; i < CurvePoints.Length - 1; i++)
            {
                curveSegments[i] = CurveFitting.GetCoefficients(CurvePoints[i], CurvePoints[i + 1]);
            }
        }

        internal static void ResolveSlopes(CurvePoint[] points)
        {
            if (points.Length == 0)
            {
                return;
            }

            var minX = points[0].X + 0.0001f;
            for (var i = 1; i < points.Length; i++)
            {
                points[i].X = MathF.Max(points[i].X, minX);
                minX = points[i].X + 0.0001f;
            }

            for (var i = 0; i < points.Length; i++)
            {
                var point = points[i];
                var previous = i > 0 ? points[i - 1] : null;
                var next = i < points.Length - 1 ? points[i + 1] : null;

                var secantFromPrevious = previous != null ? (point.Y - previous.Y) / (point.X - previous.X) : 0f;
                var secantToNext = next != null ? (next.Y - point.Y) / (next.X - point.X) : 0f;

                float secantAcross;
                if (next == null)
                {
                    secantAcross = secantFromPrevious;
                }
                else if (previous == null)
                {
                    secantAcross = secantToNext;
                }
                else
                {
                    secantAcross = (next.Y - previous.Y) / (next.X - previous.X);
                }

                switch (point.IncomingTangent)
                {
                    case CurvePoint.TangentType.Linear:
                        point.SlopeIncoming = secantFromPrevious;
                        break;
                    case CurvePoint.TangentType.Spline:
                        point.SlopeIncoming = secantAcross;
                        break;
                    case CurvePoint.TangentType.Mirror:
                        point.SlopeIncoming = 0f;
                        break;
                    case CurvePoint.TangentType.Sine:
                        point.SlopeIncoming = SineSlope(point, previous, incoming: true);
                        break;
                }

                switch (point.OutgoingTangent)
                {
                    case CurvePoint.TangentType.Linear:
                        point.SlopeOutgoing = secantToNext;
                        break;
                    case CurvePoint.TangentType.Spline:
                        point.SlopeOutgoing = secantAcross;
                        break;
                    case CurvePoint.TangentType.Mirror:
                        point.SlopeOutgoing = point.SlopeIncoming;
                        break;
                    case CurvePoint.TangentType.Sine:
                        point.SlopeOutgoing = SineSlope(point, next, incoming: false);
                        break;
                }

                if (point.IncomingTangent == CurvePoint.TangentType.Mirror)
                {
                    point.SlopeIncoming = point.SlopeOutgoing;
                }
            }
        }

        private static float SineSlope(CurvePoint point, CurvePoint? neighbour, bool incoming)
        {
            const float LowSlope = 0.041337699f;
            const float HighSlope = 1.60305f;

            if (neighbour == null)
            {
                return incoming ? -HighSlope : LowSlope;
            }

            var dx = incoming ? point.X - neighbour.X : neighbour.X - point.X;
            var dy = incoming ? point.Y - neighbour.Y : neighbour.Y - point.Y;
            var slope = incoming
                ? (dy > 0f ? LowSlope : HighSlope)
                : (dy <= 0f ? LowSlope : HighSlope);

            if (dx == 0f)
            {
                return incoming ? -slope : slope;
            }

            return (1f / dx) * (incoming ? -slope : slope);
        }
        private float ClampToDomainSpace(float value)
        {
            var min = curveDomainMin.X;
            var max = curveDomainMax.X;

            if (isLooped)
            {
                // Wrap value past min-max range
                return MathUtils.Wrap(value, min, max);
            }
            else
            {
                // Clamp to edges
                return MathF.Min(MathF.Max(value, min), max);
            }
        }

        private float ClampToDomainRange(float value)
        {
            // Inverted domain bounds resolve like the engine's successive min and max, not an exception
            return MathF.Min(MathF.Max(value, curveDomainMin.Y), curveDomainMax.Y);
        }

        public float Evaluate(float value)
        {
            if (curveSegments.Length == 0)
            {
                return 0f;
            }

            value = ClampToDomainSpace(value);

            // If coordinate is on/before the first point
            if (value <= curveSegments[0].Start.X)
            {
                return ClampToDomainRange(curveSegments[0].Start.Y);
            }
            // If coordinate is on/after the last point
            else if (value >= curveSegments[^1].End.X)
            {
                return ClampToDomainRange(curveSegments[^1].End.Y);
            }
            // If coordinate is on or between two points
            else
            {
                // Find the two points that we want to interpolate between
                for (var i = 0; i < curveSegments.Length; i++)
                {
                    // If the coordinate is in between two points (the biggie!)
                    if (curveSegments[i].IsInCurve(value))
                    {
                        value = curveSegments[i].Evaluate(value);

                        return ClampToDomainRange(value);
                    }
                }

                // I guess we just return the last point?
                return ClampToDomainRange(curveSegments[^1].End.Y);
            }
        }
    }
}
