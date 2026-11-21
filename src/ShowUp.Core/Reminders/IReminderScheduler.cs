namespace ShowUp.Core.Reminders;

/// <summary>Hands planned notifications to the OS. Re-planned on start, resume, tracking changes and reminder edits.</summary>
public interface IReminderScheduler
{
    /// <summary>False where this build can't deliver notifications yet.</summary>
    bool IsSupported { get; }

    /// <returns>True when notifications are allowed.</returns>
    Task<bool> RequestPermissionAsync();

    /// <summary>
    /// Cancels everything pending and schedules <paramref name="reminders"/> instead. Derive each notification's id from
    /// (<see cref="PlannedReminder.ReminderId"/>, <see cref="PlannedReminder.FireAt"/>) so re-planning replaces rather than duplicates.
    /// </summary>
    Task ReplaceAllAsync(IReadOnlyList<PlannedReminder> reminders);
}
