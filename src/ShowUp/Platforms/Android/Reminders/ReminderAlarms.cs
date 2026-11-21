using Android.App;
using Android.Content;
using ShowUp.Core.Reminders;

namespace ShowUp.Droid.Reminders;

/// <summary>
/// Alarm bookkeeping shared by the scheduler and the receivers. The last plan is kept as JSON in the app's files,
/// so a reboot or a clock change can put the alarms back without starting the UI.
/// </summary>
internal static class ReminderAlarms
{
    public const string ActionFire = "showup.reminders.FIRE";
    public const string ExtraTitle = "title";
    public const string ExtraBody = "body";
    public const string ExtraCode = "code";
    public const string ExtraOpen = "showup.open";

    private const string StoreFile = "planned-reminders.json";
    private static readonly Lock _lock = new();

    public static IReadOnlyList<PlannedReminder> Load(Context context)
    {
        var path = StorePath(context);
        return ReminderNotifications.Deserialize(File.Exists(path) ? File.ReadAllText(path) : null);
    }

    /// <summary>Cancels the stored plan's alarms, sets <paramref name="next"/> and stores it.</summary>
    /// <returns>True when the alarms are exact; false when Android only allows an inexact window.</returns>
    public static bool Replace(Context context, IReadOnlyList<PlannedReminder> next)
    {
        lock (_lock)
        {
            var alarms = (AlarmManager)context.GetSystemService(Context.AlarmService)!;
            foreach (var old in Load(context))
            {
                if (PendingIntentFor(context, old, PendingIntentFlags.NoCreate) is { } pending)
                {
                    alarms.Cancel(pending);
                    pending.Cancel();
                }
            }

            // Android 12+ gates exact alarms behind SCHEDULE_EXACT_ALARM, which the user can revoke.
            var exact = !OperatingSystem.IsAndroidVersionAtLeast(31) || alarms.CanScheduleExactAlarms();
            foreach (var reminder in next)
            {
                var pending = PendingIntentFor(context, reminder, PendingIntentFlags.UpdateCurrent)!;
                var at = reminder.FireAt.ToUnixTimeMilliseconds();
                if (exact)
                {
                    alarms.SetExactAndAllowWhileIdle(AlarmType.RtcWakeup, at, pending);
                }
                else
                {
                    alarms.SetAndAllowWhileIdle(AlarmType.RtcWakeup, at, pending);
                }
            }

            var path = StorePath(context);
            File.WriteAllText(path + ".tmp", ReminderNotifications.Serialize(next));
            File.Move(path + ".tmp", path, overwrite: true);
            return exact;
        }
    }

    // Extras don't take part in PendingIntent matching, so the request code alone finds the old alarm.
    private static PendingIntent? PendingIntentFor(Context context, PlannedReminder reminder, PendingIntentFlags flags)
    {
        var code = ReminderNotifications.Code(reminder);
        Intent intent = new(context, typeof(ReminderAlarmReceiver));
        intent.SetAction(ActionFire);
        intent.PutExtra(ExtraTitle, reminder.Title);
        intent.PutExtra(ExtraBody, reminder.Body);
        intent.PutExtra(ExtraCode, code);
        return PendingIntent.GetBroadcast(context, code, intent, flags | PendingIntentFlags.Immutable);
    }

    private static string StorePath(Context context) => Path.Combine(context.FilesDir!.AbsolutePath, StoreFile);
}
