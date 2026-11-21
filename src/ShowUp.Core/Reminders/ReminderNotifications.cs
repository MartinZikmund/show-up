using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ShowUp.Core.Reminders;

/// <summary>What the platform schedulers share: stable ids, caps and the stored plan.</summary>
public static class ReminderNotifications
{
    /// <summary>Every id we hand the OS starts with this, so re-planning can remove ours and only ours.</summary>
    public const string KeyPrefix = "showup-reminder-";

    /// <summary>Android channel and Windows toast group.</summary>
    public const string Channel = "reminders";

    /// <summary>"showup-reminder-{id:N}-{unix seconds}": 59 chars, inside the 64 a Windows toast tag allows.</summary>
    public static string Key(PlannedReminder reminder) => $"{KeyPrefix}{reminder.ReminderId:N}-{reminder.FireAt.ToUnixTimeSeconds()}";

    /// <summary>A stable 32-bit id of <see cref="Key"/> (FNV-1a): Android request codes and notification ids.</summary>
    public static int Code(PlannedReminder reminder)
    {
        unchecked
        {
            var hash = 2166136261;
            foreach (var b in Encoding.UTF8.GetBytes(Key(reminder)))
            {
                hash = (hash ^ b) * 16777619;
            }

            return (int)hash;
        }
    }

    /// <summary>The first <paramref name="max"/> by time (iOS keeps at most 64 pending).</summary>
    public static IReadOnlyList<PlannedReminder> Soonest(IReadOnlyList<PlannedReminder> reminders, int max) =>
        [.. reminders.OrderBy(r => r.FireAt).Take(max)];

    /// <summary>Those still ahead of <paramref name="now"/> and no further than <paramref name="window"/>.</summary>
    public static IReadOnlyList<PlannedReminder> Within(IReadOnlyList<PlannedReminder> reminders, DateTimeOffset now, TimeSpan window) =>
        [.. reminders.Where(r => r.FireAt > now && r.FireAt - now <= window).OrderBy(r => r.FireAt)];

    /// <summary>
    /// After a time zone change, keeps each reminder at the same wall-clock time in <paramref name="zone"/>;
    /// drops what is no longer ahead of <paramref name="now"/>.
    /// </summary>
    public static IReadOnlyList<PlannedReminder> Rebase(IReadOnlyList<PlannedReminder> reminders, DateTimeOffset now, TimeZoneInfo zone) =>
    [
        .. reminders
            .Select(r => r with { FireAt = ReminderPlanner.WallClock(DateOnly.FromDateTime(r.FireAt.DateTime), (int)r.FireAt.TimeOfDay.TotalMinutes, zone) })
            .Where(r => r.FireAt > now)
            .OrderBy(r => r.FireAt),
    ];

    public static string Serialize(IReadOnlyList<PlannedReminder> reminders) =>
        JsonSerializer.Serialize([.. reminders], ReminderJsonContext.Default.ListPlannedReminder);

    /// <returns>Empty for missing or unreadable JSON.</returns>
    public static IReadOnlyList<PlannedReminder> Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize(json, ReminderJsonContext.Default.ListPlannedReminder) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}

[JsonSerializable(typeof(List<PlannedReminder>))]
internal sealed partial class ReminderJsonContext : JsonSerializerContext;
