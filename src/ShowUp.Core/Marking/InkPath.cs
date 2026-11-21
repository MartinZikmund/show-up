using Windows.Foundation;

namespace ShowUp.Core.Marking;

/// <summary>A smoothed ink stroke in pixels: a start point, then quadratic segments, ending in a straight line.</summary>
public sealed record InkPath(Point Start, IReadOnlyList<(Point Control, Point End)> Curves, Point End)
{
    /// <summary>Port of <c>inkPath</c> from docs/prototypes/marker-cross.html: midpoint quadratic smoothing.</summary>
    public static InkPath? Smooth(IReadOnlyList<InkPoint> points, double width, double height)
    {
        ArgumentNullException.ThrowIfNull(points);
        if (points.Count == 0)
        {
            return null;
        }

        Point Scale(InkPoint p) => new(p.X * width, p.Y * height);

        var curves = new (Point, Point)[Math.Max(0, points.Count - 2)];
        for (var k = 1; k < points.Count - 1; k++)
        {
            Point mid = new((points[k].X + points[k + 1].X) / 2.0 * width, (points[k].Y + points[k + 1].Y) / 2.0 * height);
            curves[k - 1] = (Scale(points[k]), mid);
        }

        return new(Scale(points[0]), curves, Scale(points[^1]));
    }
}
