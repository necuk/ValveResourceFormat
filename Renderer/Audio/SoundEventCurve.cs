using System.Globalization;
using ValveKeyValue;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Audio;

/// <summary>
/// A piecewise mapping curve from sound event data (e.g. "distance_volume_mapping_curve").
/// Each point is [x, y, tangent_in, tangent_out, curve_type_left, curve_type_right]; evaluation is cubic Hermite between points.
/// </summary>
public sealed class SoundEventCurve
{
    private enum CurveTangentType
    {
        Linear = 0,
        Spline = 1,
        Free = 2,
        Mirror = 3,
        Sine = 4,
    }

    private struct Knot
    {
        public float X;
        public float Y;
        public float InTangent;
        public float OutTangent;
    }

    private const float XEpsilon = 0.0001f;
    private const float SineSteep = 1.6030499935150146f;
    private const float SineShallow = 0.04133769869804382f;

    private readonly Knot[] points;
    private readonly float outputMin;
    private readonly float outputMax;

    /// <summary>Gets the largest x value covered by the curve.</summary>
    public float MaxX => points[^1].X;

    /// <summary>
    /// Gets whether the curve actually falls off. A flat curve carries no distance information - it is a
    /// constant gain trim left in the data - and using it as an attenuation makes a sound audible at every
    /// distance, so callers treat it as its value rather than as a curve.
    /// </summary>
    public bool Attenuates => points[^1].Y < points[0].Y - 0.0001f;

    private SoundEventCurve(Knot[] points)
    {
        this.points = points;

        outputMin = points[0].Y;
        outputMax = points[0].Y;

        for (var i = 1; i < points.Length; i++)
        {
            outputMin = MathF.Min(outputMin, points[i].Y);
            outputMax = MathF.Max(outputMax, points[i].Y);
        }
    }

    /// <summary>
    /// Creates a two-point linear curve directly, e.g. to represent an authored distance range
    /// ("spread_min"/"spread_max") as a curve without going through <see cref="Parse"/>.
    /// </summary>
    internal static SoundEventCurve Linear(float x0, float y0, float x1, float y1)
    {
        return x0 <= x1
            ? new SoundEventCurve([new Knot { X = x0, Y = y0 }, new Knot { X = x1, Y = y1 }])
            : new SoundEventCurve([new Knot { X = x1, Y = y1 }, new Knot { X = x0, Y = y0 }]);
    }

    /// <summary>
    /// Returns a copy that reaches silence at <paramref name="x"/> and stays there, for events that pair a
    /// falloff curve with an authored cull distance: a curve whose last point is not silent clamps to that
    /// value, leaving the sound audible past the distance the game stops playing it at.
    /// </summary>
    internal SoundEventCurve WithCutoff(float x)
    {
        if (x <= points[0].X || (points[^1].Y <= 0f && x >= points[^1].X))
        {
            return this;
        }

        var kept = 0;

        while (kept < points.Length && points[kept].X < x)
        {
            kept++;
        }

        var cut = new Knot[kept + 1];
        points.AsSpan(0, kept).CopyTo(cut);
        cut[kept] = new Knot { X = x, Y = 0f };

        return new SoundEventCurve(cut);
    }

    /// <summary>Parses a mapping curve property from sound event data, or returns null when it is missing or empty.</summary>
    /// <param name="soundEventData">The event data holding the curve.</param>
    /// <param name="name">Property name of the curve.</param>
    /// <param name="decibels">Whether the curve's values are decibels, converted to linear gain as they are read.</param>
    public static SoundEventCurve? Parse(KVObject soundEventData, string name, bool decibels = false)
    {
        if (!soundEventData.TryGetValue(name, out var value) || value.ValueType != KVValueType.Array)
        {
            return null;
        }

        var array = soundEventData.GetArray(name);
        if (array == null || array.Count == 0)
        {
            return null;
        }

        var points = new List<(Knot Knot, CurveTangentType TypeIn, CurveTangentType TypeOut)>(array.Count);

        // Indexed rather than foreach: enumerating the interface-typed list boxes an enumerator per
        // call, and this runs three times per event constructor during cold soundscape-tree builds
        for (var i = 0; i < array.Count; i++)
        {
            var point = array[i];

            // Each point is [x, y, tangents...]; skip malformed points instead of throwing on bad data
            if (point.Count < 2)
            {
                continue;
            }

            var y = Convert.ToSingle(point[1], CultureInfo.InvariantCulture);

            points.Add((
                new Knot
                {
                    X = Convert.ToSingle(point[0], CultureInfo.InvariantCulture),
                    Y = decibels ? MathUtils.DecibelsToLinear(y) : y,
                    InTangent = point.Count > 2 ? Convert.ToSingle(point[2], CultureInfo.InvariantCulture) : 0f,
                    OutTangent = point.Count > 3 ? Convert.ToSingle(point[3], CultureInfo.InvariantCulture) : 0f,
                },
                point.Count > 4 ? (CurveTangentType)Convert.ToInt32(point[4], CultureInfo.InvariantCulture) : CurveTangentType.Linear,
                point.Count > 5 ? (CurveTangentType)Convert.ToInt32(point[5], CultureInfo.InvariantCulture) : CurveTangentType.Linear));
        }

        if (points.Count == 0)
        {
            return null;
        }

        points.Sort(static (a, b) => a.Knot.X.CompareTo(b.Knot.X));

        return new SoundEventCurve(ResolveTangents(points));
    }

    private static Knot[] ResolveTangents(List<(Knot Knot, CurveTangentType TypeIn, CurveTangentType TypeOut)> input)
    {
        var result = new Knot[input.Count];

        for (var i = 0; i < input.Count; i++)
        {
            result[i] = input[i].Knot;
        }

        if (result.Length == 1)
        {
            ResolveSingleKnotTangents(ref result[0], input[0].TypeIn, input[0].TypeOut);
            return result;
        }

        var nextMin = result[0].X + XEpsilon;

        for (var i = 1; i < result.Length; i++)
        {
            result[i].X = MathF.Max(result[i].X, nextMin);
            nextMin = result[i].X + XEpsilon;
        }

        for (var i = 0; i < result.Length; i++)
        {
            ref var knot = ref result[i];
            var (_, typeIn, typeOut) = input[i];
            var hasPrev = i > 0;
            var hasNext = i + 1 < result.Length;
            var prev = hasPrev ? result[i - 1] : default;
            var next = hasNext ? result[i + 1] : default;
            var prevSlope = hasPrev ? Slope(prev, knot) : 0f;
            var nextSlope = hasNext ? Slope(knot, next) : 0f;
            var span = hasPrev && hasNext ? Slope(prev, next) : (hasPrev ? prevSlope : nextSlope);

            knot.InTangent = typeIn switch
            {
                CurveTangentType.Linear => prevSlope,
                CurveTangentType.Spline => span,
                CurveTangentType.Mirror => 0f,
                CurveTangentType.Sine => SineTangent(hasPrev ? knot.Y - prev.Y : 0f, hasPrev ? knot.X - prev.X : 0f),
                _ => knot.InTangent,
            };

            knot.OutTangent = typeOut switch
            {
                CurveTangentType.Linear => nextSlope,
                CurveTangentType.Spline => span,
                CurveTangentType.Mirror => knot.InTangent,
                CurveTangentType.Sine => SineTangent(hasNext ? next.Y - knot.Y : 0f, hasNext ? next.X - knot.X : 0f),
                _ => knot.OutTangent,
            };

            if (typeIn == CurveTangentType.Mirror)
            {
                knot.InTangent = knot.OutTangent;
            }
        }

        return result;
    }

    private static void ResolveSingleKnotTangents(ref Knot knot, CurveTangentType typeIn, CurveTangentType typeOut)
    {
        var inTangent = typeIn is CurveTangentType.Linear or CurveTangentType.Spline ? 0f : knot.InTangent;
        var outTangent = typeOut is CurveTangentType.Linear or CurveTangentType.Spline ? 0f : knot.OutTangent;

        if (typeIn == CurveTangentType.Mirror)
        {
            inTangent = outTangent;
        }

        if (typeOut == CurveTangentType.Mirror)
        {
            outTangent = inTangent;
        }

        knot.InTangent = typeIn == CurveTangentType.Sine ? SineShallow : inTangent;
        knot.OutTangent = typeOut == CurveTangentType.Sine ? SineShallow : outTangent;
    }

    private static float Slope(in Knot from, in Knot to)
    {
        var dx = to.X - from.X;

        return dx == 0f ? 0f : (to.Y - from.Y) / dx;
    }

    private static float SineTangent(float deltaY, float deltaX)
    {
        if (deltaY < 0f)
        {
            return deltaX < 0f ? -SineSteep : -SineShallow;
        }

        return deltaX < 0f ? SineShallow : SineSteep;
    }

    /// <summary>Evaluates the curve at the given x, clamping to the first and last points.</summary>
    public float Evaluate(float x)
    {
        if (x <= points[0].X)
        {
            return points[0].Y;
        }

        if (x >= points[^1].X)
        {
            return points[^1].Y;
        }

        for (var i = 1; i < points.Length; i++)
        {
            if (x <= points[i].X)
            {
                var left = points[i - 1];
                var right = points[i];

                var width = right.X - left.X;
                var t = x - left.X;

                if (width != 0f)
                {
                    t /= width;
                }

                t = Math.Clamp(t, 0f, 1f);

                var delta = right.Y - left.Y;
                var cubic = (right.InTangent + left.OutTangent) * width - (delta + delta);
                var quadratic = (-right.InTangent - (left.OutTangent + left.OutTangent)) * width;
                var value = ((cubic * t + quadratic + delta * 3f) * t + width * left.OutTangent) * t + left.Y;

                return Math.Clamp(value, outputMin, outputMax);
            }
        }

        return points[^1].Y;
    }
}
