using CommunityToolkit.Mvvm.Messaging;
using Foundation;
using ShowUp.Core.Messages;
using ShowUp.Core.Reminders;
using UserNotifications;

namespace ShowUp.iOS.Reminders;

/// <summary>Local notifications with calendar triggers. iOS keeps at most 64 pending per app, so only the soonest 60 go in.</summary>
public sealed class IosReminderScheduler : IReminderScheduler
{
    public const int MaxPending = 60;

    // UNUserNotificationCenter holds its delegate weakly.
    private static readonly ForegroundDelegate _delegate = new();
    private readonly ILogger<IosReminderScheduler> _logger;

    public IosReminderScheduler(ILogger<IosReminderScheduler> logger)
    {
        _logger = logger;
        UNUserNotificationCenter.Current.Delegate = _delegate;
    }

    public bool IsSupported => true;

    public ReminderDeliveryLimit Limit => ReminderDeliveryLimit.None;

    public async Task<bool> RequestPermissionAsync()
    {
        var (granted, error) = await UNUserNotificationCenter.Current.RequestAuthorizationAsync(UNAuthorizationOptions.Alert | UNAuthorizationOptions.Sound);
        if (error is not null)
        {
            _logger.LogWarning("Notification authorization failed: {Error}", error.LocalizedDescription);
        }

        return granted;
    }

    public async Task<bool> IsDeniedAsync() =>
        (await UNUserNotificationCenter.Current.GetNotificationSettingsAsync()).AuthorizationStatus == UNAuthorizationStatus.Denied;

    public async Task ReplaceAllAsync(IReadOnlyList<PlannedReminder> reminders)
    {
        var center = UNUserNotificationCenter.Current;
        var pending = await center.GetPendingNotificationRequestsAsync();
        center.RemovePendingNotificationRequests([.. pending.Select(p => p.Identifier).Where(id => id.StartsWith(ReminderNotifications.KeyPrefix, StringComparison.Ordinal))]);

        var kept = ReminderNotifications.Soonest(reminders, MaxPending);
        foreach (var reminder in kept)
        {
            UNMutableNotificationContent content = new()
            {
                Title = reminder.Title,
                Body = reminder.Body,
                Sound = UNNotificationSound.Default,
            };

            // No TimeZone on the components: the trigger floats with the device's zone, keeping the wall-clock time.
            var local = TimeZoneInfo.ConvertTime(reminder.FireAt, TimeZoneInfo.Local);
            NSDateComponents when = new()
            {
                Year = local.Year,
                Month = local.Month,
                Day = local.Day,
                Hour = local.Hour,
                Minute = local.Minute,
                Second = 0,
            };

            var request = UNNotificationRequest.FromIdentifier(ReminderNotifications.Key(reminder), content, UNCalendarNotificationTrigger.CreateTrigger(when, repeats: false));
            await center.AddNotificationRequestAsync(request);
        }

        _logger.LogInformation("Scheduled {Count} of {Planned} reminder notifications", kept.Count, reminders.Count);
    }

    private sealed class ForegroundDelegate : UNUserNotificationCenterDelegate
    {
        public override void WillPresentNotification(UNUserNotificationCenter center, UNNotification notification, Action<UNNotificationPresentationOptions> completionHandler) =>
            completionHandler(UNNotificationPresentationOptions.Banner | UNNotificationPresentationOptions.List | UNNotificationPresentationOptions.Sound);

        public override void DidReceiveNotificationResponse(UNUserNotificationCenter center, UNNotificationResponse response, Action completionHandler)
        {
            if (response.Notification.Request.Identifier.StartsWith(ReminderNotifications.KeyPrefix, StringComparison.Ordinal))
            {
                WeakReferenceMessenger.Default.Send(new ReminderOpenedMessage());
            }

            completionHandler();
        }
    }
}
