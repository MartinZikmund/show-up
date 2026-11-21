using Android.App;
using Android.Content;
using Android.Runtime;
using AndroidX.Core.App;
using ShowUp.Core.Reminders;

namespace ShowUp.Droid.Reminders;

/// <summary>An alarm went off: post the reminder on the "reminders" channel. Declared in AndroidManifest.xml.</summary>
[Register("showup.reminders.ReminderAlarmReceiver")]
public sealed class ReminderAlarmReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context is null || intent?.Action != ReminderAlarms.ActionFire)
        {
            return;
        }

        var manager = NotificationManagerCompat.From(context)!;
        if (!manager.AreNotificationsEnabled())
        {
            return;
        }

        EnsureChannel(context);
        var code = intent.GetIntExtra(ReminderAlarms.ExtraCode, 0);
        var body = intent.GetStringExtra(ReminderAlarms.ExtraBody);
        var notification = new NotificationCompat.Builder(context, ReminderNotifications.Channel)
            .SetSmallIcon(Resource.Drawable.ic_stat_reminder)!
            .SetContentTitle(intent.GetStringExtra(ReminderAlarms.ExtraTitle))!
            .SetContentText(body)!
            .SetStyle(new NotificationCompat.BigTextStyle().BigText(body))!
            .SetCategory(NotificationCompat.CategoryReminder)!
            .SetAutoCancel(true)!
            .SetContentIntent(OpenToday(context, code))!
            .Build()!;

        try
        {
            manager.Notify(code, notification);
        }
        catch (Java.Lang.SecurityException)
        {
            // POST_NOTIFICATIONS revoked between the check and the post.
        }
    }

    public static void EnsureChannel(Context context)
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(26))
        {
            return;
        }

        var manager = (NotificationManager)context.GetSystemService(Context.NotificationService)!;
        if (manager.GetNotificationChannel(ReminderNotifications.Channel) is null)
        {
            manager.CreateNotificationChannel(new NotificationChannel(ReminderNotifications.Channel, context.GetString(Resource.String.ReminderChannelName), NotificationImportance.Default));
        }
    }

    private static PendingIntent OpenToday(Context context, int code)
    {
        Intent open = new(context, typeof(MainActivity));
        open.AddFlags(ActivityFlags.NewTask | ActivityFlags.ClearTop | ActivityFlags.SingleTop);
        open.PutExtra(ReminderAlarms.ExtraOpen, "today");
        return PendingIntent.GetActivity(context, code, open, PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable)!;
    }
}
