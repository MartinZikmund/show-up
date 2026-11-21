using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using ShowUp.Core.Messages;
using ShowUp.Core.Reminders;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;

namespace ShowUp.Platforms.Windows.Reminders;

/// <summary>
/// Scheduled toasts: Windows shows them with the app closed. Needs package identity; an unpackaged run reports unsupported.
/// Windows drops a scheduled toast if the PC is off for more than 5 minutes past its time.
/// </summary>
public sealed class WindowsReminderScheduler(ILogger<WindowsReminderScheduler> logger) : IReminderScheduler
{
    private static readonly Lazy<bool> _packaged = new(() =>
    {
        try
        {
            return global::Windows.ApplicationModel.Package.Current.Id is not null;
        }
        catch (Exception)
        {
            return false;
        }
    });

    public static bool IsPackaged => _packaged.Value;

    public bool IsSupported => IsPackaged;

    public ReminderDeliveryLimit Limit => ReminderDeliveryLimit.None;

    /// <summary>Call once at startup, before the window shows: routes a clicked reminder to Today.</summary>
    public static void RegisterActivation(IMessenger messenger)
    {
        if (!IsPackaged)
        {
            return;
        }

        AppNotificationManager.Default.NotificationInvoked += (_, args) =>
        {
            if (args.Arguments.TryGetValue("action", out var action) && action == "today")
            {
                messenger.Send(new ReminderOpenedMessage());
            }
        };
        AppNotificationManager.Default.Register();
    }

    public Task<bool> RequestPermissionAsync() =>
        Task.FromResult(IsPackaged && AppNotificationManager.Default.Setting == AppNotificationSetting.Enabled);

    public Task ReplaceAllAsync(IReadOnlyList<PlannedReminder> reminders)
    {
        var notifier = ToastNotificationManager.CreateToastNotifier();
        foreach (var scheduled in notifier.GetScheduledToastNotifications())
        {
            if (scheduled.Group == ReminderNotifications.Channel)
            {
                notifier.RemoveFromSchedule(scheduled);
            }
        }

        // AddToSchedule throws for a time already past; leave a margin for the clock moving on.
        var soon = DateTimeOffset.Now.AddSeconds(5);
        var count = 0;
        foreach (var reminder in reminders.Where(r => r.FireAt > soon))
        {
            var payload = new AppNotificationBuilder()
                .AddArgument("action", "today")
                .AddText(reminder.Title)
                .AddText(reminder.Body)
                .BuildNotification()
                .Payload;
            XmlDocument xml = new();
            xml.LoadXml(payload);
            notifier.AddToSchedule(new ScheduledToastNotification(xml, reminder.FireAt)
            {
                Tag = ReminderNotifications.Key(reminder),
                Group = ReminderNotifications.Channel,
            });
            count++;
        }

        logger.LogInformation("Scheduled {Count} reminder toasts; next at {Next}", count, reminders.FirstOrDefault(r => r.FireAt > soon)?.FireAt);
        return Task.CompletedTask;
    }
}
