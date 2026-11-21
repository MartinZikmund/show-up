namespace ShowUp.Core.Services;

/// <summary>The calendar's small input dialog.</summary>
public interface ICalendarPrompts
{
    /// <summary>Days (1-30) to push the rest of the schedule back, or null when cancelled.</summary>
    Task<int?> AskShiftDaysAsync();
}
