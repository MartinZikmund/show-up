namespace ShowUp.Core.Reminders;

/// <summary>Hands planned notifications to the OS. Re-planned on start, resume, tracking changes and reminder edits.</summary>
public interface IReminderScheduler
{
    /// <summary>False where this build can't deliver notifications yet.</summary>
    bool IsSupported { get; }

    /// <returns>True when notifications are allowed.</returns>
    Task<bool> RequestPermissionAsync();

    /// <summary>Cancels everything pending and schedules <paramref name="reminders"/> instead.</summary>
    Task ReplaceAllAsync(IReadOnlyList<PlannedReminder> reminders);
}
