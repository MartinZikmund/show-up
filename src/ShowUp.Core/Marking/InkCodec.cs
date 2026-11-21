using System.Text.Json;
using System.Text.Json.Serialization;

namespace ShowUp.Core.Marking;

/// <summary>
/// Compact storage form of user ink: <c>[[x,y,x,y,…],…]</c>, one array per stroke, 3 decimals.
/// Strokes are simplified (Ramer–Douglas–Peucker) and capped at <see cref="MaxPoints"/> in total.
/// </summary>
public static class InkCodec
{
    public const int MaxPoints = 600;
    private const float Epsilon = 0.004f;

    public static string Encode(IReadOnlyList<InkStroke> strokes)
    {
        ArgumentNullException.ThrowIfNull(strokes);

        var simplified = strokes.Where(s => s.Points.Count > 0).Select(s => Simplify(s.Points)).ToList();
        var budget = Budget(simplified.Select(s => s.Count).ToList());

        double[][] data = new double[budget.Count][];
        for (var i = 0; i < budget.Count; i++)
        {
            var points = Subsample(simplified[i], budget[i]);
            var coordinates = new double[points.Count * 2];
            for (var k = 0; k < points.Count; k++)
            {
                coordinates[2 * k] = Quantize(points[k].X);
                coordinates[2 * k + 1] = Quantize(points[k].Y);
            }

            data[i] = coordinates;
        }

        return JsonSerializer.Serialize(data, InkJsonContext.Default.DoubleArrayArray);
    }

    /// <summary>Never throws: unreadable ink renders as no ink rather than breaking a calendar cell.</summary>
    public static IReadOnlyList<InkStroke> Decode(string json)
    {
        double[][]? data;
        try
        {
            data = JsonSerializer.Deserialize(json, InkJsonContext.Default.DoubleArrayArray);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or ArgumentException)
        {
            return [];
        }

        if (data is null)
        {
            return [];
        }

        List<InkStroke> strokes = [];
        foreach (var coordinates in data)
        {
            if (coordinates is not { Length: >= 2 })
            {
                continue;
            }

            var points = new InkPoint[coordinates.Length / 2];
            for (var k = 0; k < points.Length; k++)
            {
                points[k] = new(Clamp(coordinates[2 * k]), Clamp(coordinates[2 * k + 1]));
            }

            strokes.Add(new(points));
        }

        return strokes;
    }

    private static double Quantize(float value) => Math.Round(Math.Clamp(value, 0, 1), 3, MidpointRounding.AwayFromZero);

    private static float Clamp(double value) => (float)Math.Clamp(value, 0, 1);

    // Points per stroke after the cap: proportional, each stroke keeps its ends.
    private static List<int> Budget(List<int> counts)
    {
        var total = counts.Sum();
        if (total <= MaxPoints)
        {
            return counts;
        }

        var budget = counts.Select(n => Math.Clamp(n * MaxPoints / total, Math.Min(n, 2), n)).ToList();
        while (budget.Sum() > MaxPoints)
        {
            var largest = budget.IndexOf(budget.Max());
            if (budget[largest] <= 2)
            {
                // Hundreds of tiny strokes: the last ones go.
                budget.RemoveAt(budget.Count - 1);
                continue;
            }

            budget[largest]--;
        }

        return budget;
    }

    private static IReadOnlyList<InkPoint> Subsample(IReadOnlyList<InkPoint> points, int count)
    {
        if (count >= points.Count)
        {
            return points;
        }

        var result = new InkPoint[count];
        for (var i = 0; i < count; i++)
        {
            result[i] = points[(int)Math.Round((double)i * (points.Count - 1) / (count - 1))];
        }

        return result;
    }

    private static List<InkPoint> Simplify(IReadOnlyList<InkPoint> points)
    {
        if (points.Count <= 2)
        {
            return [.. points];
        }

        var keep = new bool[points.Count];
        keep[0] = keep[^1] = true;
        Stack<(int First, int Last)> pending = new();
        pending.Push((0, points.Count - 1));
        while (pending.Count > 0)
        {
            var (first, last) = pending.Pop();
            var (index, distance) = (-1, 0f);
            for (var i = first + 1; i < last; i++)
            {
                var d = DistanceToSegment(points[i], points[first], points[last]);
                if (d > distance)
                {
                    (index, distance) = (i, d);
                }
            }

            if (index >= 0 && distance > Epsilon)
            {
                keep[index] = true;
                pending.Push((first, index));
                pending.Push((index, last));
            }
        }

        return points.Where((_, i) => keep[i]).ToList();
    }

    private static float DistanceToSegment(InkPoint p, InkPoint a, InkPoint b)
    {
        float dx = b.X - a.X, dy = b.Y - a.Y;
        var lengthSquared = dx * dx + dy * dy;
        var t = lengthSquared == 0 ? 0 : Math.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / lengthSquared, 0, 1);
        float ex = p.X - (a.X + t * dx), ey = p.Y - (a.Y + t * dy);
        return MathF.Sqrt(ex * ex + ey * ey);
    }
}

[JsonSerializable(typeof(double[][]))]
internal sealed partial class InkJsonContext : JsonSerializerContext;
