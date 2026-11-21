using Windows.Foundation;

namespace ShowUp.Core.Marking;

/// <summary>Port of <c>strokePath</c> from docs/prototypes/marker-cross.html.</summary>
public static class MarkerGeometry
{
    private const double MarginRatio = 0.16;
    private const double ReferenceCellSize = 96;
    private const double DefaultWobble = 4;
    private const double DefaultOvershoot = 8;

    public static (MarkerStroke First, MarkerStroke Second) Create(int seed, double width, double height, double wobble = DefaultWobble, double overshoot = DefaultOvershoot)
    {
        var next = Random(seed);
        var margin = Math.Min(width, height) * MarginRatio;

        var first = Stroke(margin, margin, width - margin, height - margin, next, wobble, overshoot);
        var second = Stroke(width - margin, margin, margin, height - margin, next, wobble, overshoot);
        return (first, second);
    }

    /// <summary>
    /// <see cref="Create"/> with wobble and overshoot scaled like the stroke width (min side / 96, never below 1),
    /// so a big X is as loose as a small one instead of ruler-straight.
    /// </summary>
    public static (MarkerStroke First, MarkerStroke Second) ForCell(int seed, double width, double height)
    {
        var scale = Math.Max(1, Math.Min(width, height) / ReferenceCellSize);
        return Create(seed, width, height, DefaultWobble * scale, DefaultOvershoot * scale);
    }

    /// <summary>Per-stroke width variation (0.92-1.08), stable for a seed, so two strokes don't look stamped.</summary>
    public static double WidthFactor(int seed, int strokeIndex)
    {
        var hash = (uint)seed * 2654435761u ^ (uint)(strokeIndex + 1) * 40503u;
        hash ^= hash >> 15;
        return 0.92 + hash % 1601 / 10000.0;
    }

    /// <summary>FNV-1a over the GUID bytes: identical on every platform and runtime, unlike <see cref="Guid.GetHashCode"/>.</summary>
    public static int SeedFrom(Guid id)
    {
        Span<byte> bytes = stackalloc byte[16];
        id.TryWriteBytes(bytes);

        var hash = 2166136261u;
        foreach (var b in bytes)
        {
            hash = (hash ^ b) * 16777619u;
        }

        return (int)(hash & int.MaxValue);
    }

    private static MarkerStroke Stroke(double x1, double y1, double x2, double y2, Func<double> r, double w, double ov)
    {
        double dx = x2 - x1, dy = y2 - y1;
        var len = Math.Sqrt(dx * dx + dy * dy);
        double ux = dx / len, uy = dy / len;

        // Draw order matters: it must consume the generator exactly like the prototype.
        var sx = x1 - ux * ov * r() + (r() - .5) * w;
        var sy = y1 - uy * ov * r() + (r() - .5) * w;
        var ex = x2 + ux * ov * r() + (r() - .5) * w;
        var ey = y2 + uy * ov * r() + (r() - .5) * w;
        var bow = (r() - .5) * w * 3;
        var cx = (sx + ex) / 2 - uy * bow;
        var cy = (sy + ey) / 2 + ux * bow;

        return new(new Point(sx, sy), new Point(cx, cy), new Point(ex, ey));
    }

    // The prototype's linear congruential generator, in integers so it can't drift from the JS doubles.
    private static Func<double> Random(int seed)
    {
        var s = (long)(seed & int.MaxValue) * 9301 + 49297;
        return () =>
        {
            s = (s * 9301 + 49297) % 233280;
            return s / 233280.0;
        };
    }
}
