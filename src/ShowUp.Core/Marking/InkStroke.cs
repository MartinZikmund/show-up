namespace ShowUp.Core.Marking;

/// <summary>A point of user ink, normalized 0..1 to the drawing square.</summary>
public readonly record struct InkPoint(float X, float Y);

/// <summary>One finger/pen/mouse stroke, from press to release.</summary>
public sealed record InkStroke(IReadOnlyList<InkPoint> Points);
