using Android;
using AndroidX.Core.App;
using ShowUp.Core.Reminders;

namespace ShowUp.Droid.Reminders;

/// <summary>AlarmManager alarms that post a notification; they survive the app closing, and reboots via <see cref="ReminderRescheduleReceiver"/>.</summary>
public sealed class AndroidReminderScheduler(ILogger<AndroidReminderScheduler> logger) : IReminderScheduler
{
    private static Android.Content.Context Context => Android.App.Application.Context;

    public bool IsSupported => true;

    public ReminderDeliveryLimit Limit => ReminderDeliveryLimit.None;

    public async Task<bool> RequestPermissionAsync()
    {
        // Android 13+ asks at runtime; earlier versions allow notifications unless the user turned them off.
        if (OperatingSystem.IsAndroidVersionAtLeast(33) && Uno.UI.BaseActivity.Current is { } activity)
        {
            await RuntimePermission.RequestAsync(activity, Manifest.Permission.PostNotifications);
        }

        ReminderAlarmReceiver.EnsureChannel(Context);
        return NotificationManagerCompat.From(Context)!.AreNotificationsEnabled();
    }

    public Task<bool> IsDeniedAsync() => Task.FromResult(!NotificationManagerCompat.From(Context)!.AreNotificationsEnabled());

    public Task ReplaceAllAsync(IReadOnlyList<PlannedReminder> reminders) => Task.Run(() =>
    {
        var exact = ReminderAlarms.Replace(Context, reminders);
        logger.LogInformation("Scheduled {Count} reminder alarms ({Kind}); next at {Next}", reminders.Count, exact ? "exact" : "inexact", reminders.Count > 0 ? reminders.Min(r => r.FireAt) : null);
    });
}
