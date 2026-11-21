namespace ShowUp.Core.Services;

/// <summary>The calendar's small input dialogs.</summary>
public interface ICalendarPrompts
{
    /// <summary>Days (1-30) to push the rest of the schedule back, or null when cancelled.</summary>
    Task<int?> AskShiftDaysAsync();

    /// <summary>The bonus workout's title, or null when cancelled.</summary>
    Task<string?> AskBonusTitleAsync();
}
