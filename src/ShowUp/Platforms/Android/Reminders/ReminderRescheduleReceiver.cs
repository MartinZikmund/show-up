using Android.Content;
using Android.Runtime;
using ShowUp.Core.Reminders;

namespace ShowUp.Droid.Reminders;

/// <summary>
/// Reboots clear alarms, and clock or zone changes shift them: put the stored plan back, at the same wall-clock times.
/// Declared in AndroidManifest.xml with its intent filters.
/// </summary>
[Register("showup.reminders.ReminderRescheduleReceiver")]
public sealed class ReminderRescheduleReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context is null)
        {
            return;
        }

        // .NET caches the zone for the life of the process.
        TimeZoneInfo.ClearCachedData();
        var stored = ReminderAlarms.Load(context);
        ReminderAlarms.Replace(context, ReminderNotifications.Rebase(stored, DateTimeOffset.UtcNow, TimeZoneInfo.Local));
        Android.Util.Log.Info("ShowUp", $"Reminders rescheduled after {intent?.Action}: {stored.Count} stored");
    }
}
