namespace ShowUp.Core.Marking;

/// <summary>
/// Opens the draw moment for one session. Get the stroke counts from <see cref="MarkerAppearance.Outcomes"/>:
/// <c>Info.StrokesBefore</c> = Before, <c>Info.StrokesAfter</c> = AfterAuto (played on "Do it for me"),
/// <see cref="DrawnStrokesAfter"/> = AfterDrawn (a drawn session can complete a day that has auto sessions,
/// which then lands the full X).
/// </summary>
/// <param name="Info">The day and the caption numbers as they will be once this session is crossed.</param>
/// <param name="PriorInks">Saved ink (<see cref="InkCodec"/> JSON) of the day's other sessions, shown at 50%.</param>
/// <param name="DrawnStrokesAfter">Generated strokes after a drawn cross; usually <c>Info.StrokesBefore</c>.</param>
public sealed record CrossMomentDrawRequest(CrossMomentInfo Info, IReadOnlyList<string> PriorInks, int DrawnStrokesAfter);

public enum CrossMomentOutcome
{
    Cancelled,

    /// <summary>"Do it for me": persist without ink; the generated X is drawn.</summary>
    Auto,

    /// <summary>The user drew it: persist <see cref="CrossMomentResult.InkJson"/>.</summary>
    Drawn,
}

public sealed record CrossMomentResult(CrossMomentOutcome Outcome, string? InkJson = null)
{
    public static CrossMomentResult Cancelled { get; } = new(CrossMomentOutcome.Cancelled);

    public static CrossMomentResult Auto { get; } = new(CrossMomentOutcome.Auto);

    public bool Crossed => Outcome != CrossMomentOutcome.Cancelled;

    public static CrossMomentResult Drawn(string inkJson) => new(CrossMomentOutcome.Drawn, inkJson);
}
