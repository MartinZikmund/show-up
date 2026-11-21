using ShowUp.Core.Reminders;

namespace ShowUp.Services.Reminders;

/// <summary>Placeholder until a head gets real notifications: reminders can be set up but nothing is delivered.</summary>
public sealed class NullReminderScheduler : IReminderScheduler
{
    public bool IsSupported => false;

    public Task<bool> RequestPermissionAsync() => Task.FromResult(false);

    public Task ReplaceAllAsync(IReadOnlyList<PlannedReminder> reminders) => Task.CompletedTask;
}
