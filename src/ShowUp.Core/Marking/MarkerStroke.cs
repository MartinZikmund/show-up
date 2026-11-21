using Windows.Foundation;

namespace ShowUp.Core.Marking;

/// <summary>One hand-drawn marker stroke: a quadratic Bezier.</summary>
public readonly record struct MarkerStroke(Point Start, Point Control, Point End)
{
    private const int LengthSegments = 32;

    public double Length
    {
        get
        {
            var length = 0.0;
            var previous = Start;
            for (var i = 1; i <= LengthSegments; i++)
            {
                var next = PointAt((double)i / LengthSegments);
                length += Distance(previous, next);
                previous = next;
            }

            return length;
        }
    }

    public Point PointAt(double t)
    {
        var u = 1 - t;
        return new(
            u * u * Start.X + 2 * u * t * Control.X + t * t * End.X,
            u * u * Start.Y + 2 * u * t * Control.Y + t * t * End.Y);
    }

    /// <summary>The part of the stroke drawn so far, from <see cref="Start"/> to parameter <paramref name="t"/> (de Casteljau split).</summary>
    public MarkerStroke Trim(double t)
    {
        t = Math.Clamp(t, 0, 1);
        return new(Start, Lerp(Start, Control, t), PointAt(t));
    }

    /// <summary>The same stroke shifted sideways (perpendicular to start-end) by <paramref name="distance"/>.</summary>
    public MarkerStroke Offset(double distance)
    {
        var (dx, dy) = (End.X - Start.X, End.Y - Start.Y);
        var length = Math.Sqrt(dx * dx + dy * dy);
        if (length == 0)
        {
            return this;
        }

        var (nx, ny) = (-dy / length * distance, dx / length * distance);
        return new(Shift(Start, nx, ny), Shift(Control, nx, ny), Shift(End, nx, ny));
    }

    private static Point Shift(Point p, double dx, double dy) => new(p.X + dx, p.Y + dy);

    private static Point Lerp(Point a, Point b, double t) => new(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);

    private static double Distance(Point a, Point b) => Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y));
}
