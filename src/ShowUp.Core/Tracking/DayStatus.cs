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
    bool IsRestDay)
{
    public DayProgress Progress =>
        CompletedSessions.Count == 0 ? DayProgress.Empty
        : CompletedSessions.Count >= SessionCount ? DayProgress.Complete
        : DayProgress.Partial;

    /// <summary>Marker strokes drawn on the calendar: complete is a full X, half of two sessions is one stroke.</summary>
    public int MarkStrokes => Progress == DayProgress.Complete ? 2 : SessionCount == 2 && CompletedSessions.Count == 1 ? 1 : 0;

    public bool ShowsSessionTicks => SessionCount >= 3;

    public bool IsMissed(DateOnly today) => Date < today && Progress != DayProgress.Complete && !IsRestDay;
}
