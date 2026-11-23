namespace ShowUp.Core.ViewModels;

/// <summary>A program week: one calendar row of up to seven days.</summary>
public sealed class CalendarWeekViewModel(string phaseName, int weekNumber, bool isPhaseStart, string weekLabel, IReadOnlyList<CalendarDayViewModel> days)
{
    public string PhaseName { get; } = phaseName;

    public int WeekNumber { get; } = weekNumber;

    /// <summary>The first week of its phase, which carries the phase header, unless the phase is named exactly like the week.</summary>
    public bool IsPhaseStart { get; } = isPhaseStart;

    public string WeekLabel { get; } = weekLabel;

    public IReadOnlyList<CalendarDayViewModel> Days { get; } = days;
}
