namespace ShowUp.Core.Tracking;

public enum DayProgress
{
    Empty,
    Partial,
    Complete,
}

public sealed partial record DayStatus(
    int DayIndex,
    DateOnly Date,
    int SessionCount,
    IReadOnlySet<int> CompletedSessions,
    bool IsRestDay,
    IReadOnlySet<int>? OptionalSessionIndices = null)
{
    public IReadOnlySet<int> OptionalSessions => OptionalSessionIndices ?? new HashSet<int>();

    public int RequiredCount => SessionCount - OptionalSessions.Count(i => i >= 0 && i < SessionCount);

    private int RequiredDone => CompletedSessions.Count(i => !OptionalSessions.Contains(i));

    /// <summary>Rest day, or a day whose sessions are all optional.</summary>
    public bool CountsAsRest => IsRestDay || RequiredCount == 0;

    public DayProgress Progress =>
        RequiredCount == 0 || RequiredDone == 0 ? DayProgress.Empty
        : RequiredDone >= RequiredCount ? DayProgress.Complete
        : DayProgress.Partial;

    /// <summary>Marker strokes drawn on the calendar: complete is a full X, half of two required sessions is one stroke.</summary>
    public int MarkStrokes => Progress == DayProgress.Complete ? 2 : RequiredCount == 2 && RequiredDone == 1 ? 1 : 0;

    public bool ShowsSessionTicks => SessionCount >= 3;

    public bool IsMissed(DateOnly today) => Date < today && Progress != DayProgress.Complete && !CountsAsRest;
}
