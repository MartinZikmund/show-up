namespace ShowUp.Core.Marking;

/// <summary>How tapping a day crosses it off.</summary>
public enum CrossOffStyle
{
    /// <summary>The user draws the cross (X, slash, scribble) on a big square.</summary>
    DrawYourself,

    /// <summary>The generated marker X lands on tap (cross moment per <see cref="CrossMomentMode"/>).</summary>
    AutoMarker,
}
