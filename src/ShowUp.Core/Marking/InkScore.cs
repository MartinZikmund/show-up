namespace ShowUp.Core.Marking;

/// <summary>Port of <c>inkScore</c> from docs/prototypes/marker-cross.html: enough ink, spread across the square.</summary>
public static class InkScore
{
    private const double FullLength = 0.75;
    private const double FullSpread = 0.4;
    private const double SpreadGate = 0.2;

    /// <summary>0..1 (1 = counts as crossed).</summary>
    public static double Of(IReadOnlyList<InkStroke> strokes)
    {
        ArgumentNullException.ThrowIfNull(strokes);

        double length = 0, minX = 1, minY = 1, maxX = 0, maxY = 0;
        foreach (var stroke in strokes)
        {
            var points = stroke.Points;
            for (var k = 0; k < points.Count; k++)
            {
                var (x, y) = (points[k].X, points[k].Y);
                minX = Math.Min(minX, x);
                minY = Math.Min(minY, y);
                maxX = Math.Max(maxX, x);
                maxY = Math.Max(maxY, y);
                if (k > 0)
                {
                    double dx = x - points[k - 1].X, dy = y - points[k - 1].Y;
                    length += Math.Sqrt(dx * dx + dy * dy);
                }
            }
        }

        var spread = length > SpreadGate ? Math.Min(Math.Min((maxX - minX) / FullSpread, (maxY - minY) / FullSpread), 1) : 0;
        return Math.Min(length / FullLength, 1) * 0.6 + spread * 0.4;
    }

    public static bool Counts(IReadOnlyList<InkStroke> strokes) => Of(strokes) >= 1;
}
