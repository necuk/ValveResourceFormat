using System.Globalization;
using System.Linq;
using ValveKeyValue;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Audio;

/// <summary>
/// A piecewise mapping curve from sound event data (e.g. "distance_volume_mapping_curve").
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

    private sealed class Knot
    {
        public float X;
        public float Y;
        public float InTangent;
        public float OutTangent;
    }

    private readonly Knot[] knots;

    /// <summary>Gets the largest x value covered by the curve.</summary>
    public float MaxX => knots[^1].X;

    /// <summary>
    /// Gets whether the curve actually falls off. A flat curve carries no distance information - it is a
    /// constant gain trim left in the data - and using it as an attenuation makes a sound audible at every
    /// distance, so callers treat it as its value rather than as a curve.
    /// </summary>
    public bool Attenuates => knots[^1].Y < knots[0].Y - 0.0001f;

    private SoundEventCurve(Knot[] knots)
    {
        this.knots = knots;
    }

    /// <summary>
    /// Creates a two-point linear curve directly, e.g. to represent an authored distance range
    /// ("spread_min"/"spread_max") as a curve without going through <see cref="Parse"/>.
    /// </summary>
    internal static SoundEventCurve Linear(float x0, float y0, float x1, float y1)
    {
        var knots = new[]
        {
            new Knot { X = x0, Y = y0, InTangent = 0, OutTangent = 0 },
            new Knot { X = x1, Y = y1, InTangent = 0, OutTangent = 0 },
        };
        return new SoundEventCurve(knots);
    }

    /// <summary>
    /// Returns a copy that reaches silence at <paramref name="x"/> and stays there, for events that pair a
    /// falloff curve with an authored cull distance: a curve whose last point is not silent clamps to that
    /// value, leaving the sound audible past the distance the game stops playing it at.
    /// </summary>
    internal SoundEventCurve WithCutoff(float x)
    {
        if (x <= knots[0].X || (knots[^1].Y <= 0f && x >= knots[^1].X))
        {
            return this;
        }

        var kept = 0;

        while (kept < knots.Length && knots[kept].X < x)
        {
            kept++;
        }

        var cut = new Knot[kept + 1];
        Array.Copy(knots, 0, cut, 0, kept);
        cut[kept] = new Knot { X = x, Y = 0f, InTangent = 0, OutTangent = 0 };

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

        var knots = new List<Knot>(array.Count);
        var types = new List<(CurveTangentType incoming, CurveTangentType outgoing)>(array.Count);

        for (var i = 0; i < array.Count; i++)
        {
            var point = array[i];

            if (point.Count < 2)
            {
                continue;
            }

            var x = Convert.ToSingle(point[0], CultureInfo.InvariantCulture);
            var y = Convert.ToSingle(point[1], CultureInfo.InvariantCulture);

            var inTangent = point.Count > 2 ? Convert.ToSingle(point[2], CultureInfo.InvariantCulture) : 0f;
            var outTangent = point.Count > 3 ? Convert.ToSingle(point[3], CultureInfo.InvariantCulture) : 0f;
            var typeIn = point.Count > 4 ? (CurveTangentType)Convert.ToInt32(point[4], CultureInfo.InvariantCulture) : CurveTangentType.Linear;
            var typeOut = point.Count > 5 ? (CurveTangentType)Convert.ToInt32(point[5], CultureInfo.InvariantCulture) : CurveTangentType.Linear;

            knots.Add(new Knot
            {
                X = x,
                Y = decibels ? MathUtils.DecibelsToLinear(y) : y,
                InTangent = inTangent,
                OutTangent = outTangent,
            });
            types.Add((typeIn, typeOut));
        }

        if (knots.Count == 0)
        {
            return null;
        }

        var sortedIndices = Enumerable.Range(0, knots.Count).OrderBy(i => knots[i].X).ToArray();
        var sortedKnots = sortedIndices.Select(i => knots[i]).ToArray();
        var sortedTypes = sortedIndices.Select(i => types[i]).ToArray();

        var preprocessed = PreprocessTangents(sortedKnots, sortedTypes);

        return new SoundEventCurve(preprocessed);
    }

    private static Knot[] PreprocessTangents(Knot[] inputKnots, (CurveTangentType incoming, CurveTangentType outgoing)[] inputTypes)
    {
        var result = new Knot[inputKnots.Length];
        Array.Copy(inputKnots, result, inputKnots.Length);

        const float X_EPSILON = 0.00009999999747378752f;
        const float SINE_STEEP_NEG = -1.6030499935150146f;
        const float SINE_SHALLOW_NEG = -0.04133769869804382f;
        const float SINE_SHALLOW_POS = 0.04133769869804382f;
        const float SINE_STEEP_POS = 1.6030499935150146f;

        if (result.Length > 1)
        {
            var nextMin = result[0].X + X_EPSILON;
            for (var i = 1; i < result.Length; i++)
            {
                result[i].X = MathF.Max(result[i].X, nextMin);
                nextMin = result[i].X + X_EPSILON;
            }

            for (var i = 0; i < result.Length; i++)
            {
                var k = result[i];
                var prev = i > 0 ? Slope(result[i - 1], k) : 0f;
                var next = i + 1 < result.Length ? Slope(k, result[i + 1]) : 0f;
                var span = (i > 0 && i + 1 < result.Length) ? Slope(result[i - 1], result[i + 1]) : (i > 0 ? prev : next);

                switch (inputTypes[i].incoming)
                {
                    case CurveTangentType.Linear:
                        k.InTangent = prev;
                        break;
                    case CurveTangentType.Spline:
                        k.InTangent = span;
                        break;
                    case CurveTangentType.Mirror:
                        k.InTangent = 0f;
                        break;
                    case CurveTangentType.Sine:
                        {
                            var prevDeltaY = i > 0 ? k.Y - result[i - 1].Y : 0f;
                            var prevDeltaX = i > 0 ? k.X - result[i - 1].X : 0f;
                            k.InTangent = SineIncoming(prevDeltaY, prevDeltaX, SINE_STEEP_NEG, SINE_SHALLOW_NEG, SINE_SHALLOW_POS, SINE_STEEP_POS);
                            break;
                        }
                    case CurveTangentType.Free:
                    default:
                        break;
                }

                switch (inputTypes[i].outgoing)
                {
                    case CurveTangentType.Linear:
                        k.OutTangent = next;
                        break;
                    case CurveTangentType.Spline:
                        k.OutTangent = span;
                        break;
                    case CurveTangentType.Mirror:
                        k.OutTangent = k.InTangent;
                        break;
                    case CurveTangentType.Sine:
                        {
                            var nextDeltaY = i + 1 < result.Length ? result[i + 1].Y - k.Y : 0f;
                            var nextDeltaX = i + 1 < result.Length ? result[i + 1].X - k.X : 0f;
                            k.OutTangent = SineOutgoing(nextDeltaY, nextDeltaX, SINE_STEEP_NEG, SINE_SHALLOW_NEG, SINE_SHALLOW_POS, SINE_STEEP_POS);
                            break;
                        }
                    case CurveTangentType.Free:
                    default:
                        break;
                }

                if (inputTypes[i].incoming == CurveTangentType.Mirror)
                {
                    k.InTangent = k.OutTangent;
                }

                result[i] = k;
            }
        }
        else if (result.Length == 1)
        {
            var k = result[0];
            var t = inputTypes[0];

            if (t.incoming == CurveTangentType.Linear || t.incoming == CurveTangentType.Spline)
            {
                k.InTangent = 0f;
            }

            if (t.outgoing == CurveTangentType.Linear || t.outgoing == CurveTangentType.Spline)
            {
                k.OutTangent = 0f;
            }

            if (t.incoming == CurveTangentType.Mirror)
            {
                k.InTangent = k.OutTangent;
            }

            if (t.outgoing == CurveTangentType.Mirror)
            {
                k.OutTangent = k.InTangent;
            }

            if (t.incoming == CurveTangentType.Sine)
            {
                k.InTangent = SINE_SHALLOW_POS;
            }

            if (t.outgoing == CurveTangentType.Sine)
            {
                k.OutTangent = SINE_SHALLOW_POS;
            }

            result[0] = k;
        }

        return result;
    }

    private static float Slope(Knot k1, Knot k2)
    {
        var dx = k2.X - k1.X;
        if (dx == 0f)
        {
            return 0f;
        }
        return (k2.Y - k1.Y) / dx;
    }

    private static float SineIncoming(float deltaY, float deltaX, float steepNeg, float shallowNeg, float shallowPos, float steepPos)
    {
        if (deltaY < 0f)
        {
            return deltaX < 0f ? steepNeg : shallowNeg;
        }
        else
        {
            return deltaX < 0f ? shallowPos : steepPos;
        }
    }

    private static float SineOutgoing(float deltaY, float deltaX, float steepNeg, float shallowNeg, float shallowPos, float steepPos)
    {
        if (deltaY < 0f)
        {
            return deltaX < 0f ? steepNeg : shallowNeg;
        }
        else
        {
            return deltaX < 0f ? shallowPos : steepPos;
        }
    }

    /// <summary>Evaluates the curve at the given x, clamping to the first and last points.</summary>
    public float Evaluate(float x)
    {
        if (knots.Length == 0)
        {
            return 0f;
        }

        if (x <= knots[0].X)
        {
            return knots[0].Y;
        }

        if (x >= knots[^1].X)
        {
            return knots[^1].Y;
        }

        var index = FindSegmentIndex(x);
        var left = knots[index];
        var right = knots[index + 1];

        var leftX = left.X;
        var leftY = left.Y;
        var rightX = right.X;
        var rightY = right.Y;
        var rightIn = right.InTangent;
        var leftOut = left.OutTangent;

        var width = rightX - leftX;
        var t = x - leftX;
        if (width != 0f)
        {
            t = t / width;
        }
        t = MathF.Max(0f, MathF.Min(1f, t));

        var delta = rightY - leftY;
        var twoDelta = delta + delta;
        var threeDelta = delta * 3f;

        var cubic = rightIn + leftOut;
        cubic = cubic * width;
        cubic = cubic - twoDelta;

        var quadratic = -rightIn;
        quadratic = quadratic - (leftOut + leftOut);
        quadratic = quadratic * width;

        var value = cubic * t;
        value = value + quadratic;
        value = value + threeDelta;
        value = value * t;
        value = value + (width * leftOut);
        value = value * t;
        value = value + leftY;

        var outputMin = knots[0].Y;
        var outputMax = knots[0].Y;
        foreach (var k in knots)
        {
            if (k.Y < outputMin) outputMin = k.Y;
            if (k.Y > outputMax) outputMax = k.Y;
        }

        return MathF.Max(outputMin, MathF.Min(outputMax, value));
    }

    /// <summary>Creates a curve from point coordinates and tangent types, bypassing KVObject parsing. Test-only helper.</summary>
    public static SoundEventCurve CreateTestCurve(
        (float x, float y)[] points,
        (int typeIn, int typeOut)[] types)
    {
        var knots = points.Select(p => new Knot { X = p.x, Y = p.y, InTangent = 0, OutTangent = 0 }).ToArray();
        var curveTypes = types.Select(t => ((CurveTangentType)t.typeIn, (CurveTangentType)t.typeOut)).ToArray();
        var preprocessed = PreprocessTangents(knots, curveTypes);
        return new SoundEventCurve(preprocessed);
    }

    private int FindSegmentIndex(float x)
    {
        if (x <= knots[0].X)
        {
            return 0;
        }

        if (x >= knots[^1].X)
        {
            return knots.Length - 2;
        }

        var low = 0;
        var high = knots.Length - 1;
        while (low + 1 < high)
        {
            var middle = (low + high) >> 1;
            if (x > knots[middle].X)
            {
                low = middle;
            }
            else
            {
                high = middle;
            }
        }
        return low;
    }
}
