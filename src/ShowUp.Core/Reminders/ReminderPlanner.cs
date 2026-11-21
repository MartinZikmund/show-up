using ShowUp.Core.Storage;

namespace ShowUp.Core.Reminders;

public static class ReminderPlanner
{
    /// <summary>
    /// The notifications for the next <paramref name="days"/> calendar days (today included while its time is ahead),
    /// soonest first. Days already fully crossed, or with nothing due, get none.
    /// </summary>
    public static IReadOnlyList<PlannedReminder> Plan(
        IReadOnlyList<ReminderEntity> reminders,
        DateTimeOffset nowLocal,
        TimeZoneInfo zone,
        int days,
        Func<DateOnly, ReminderDay> dayInfo,
        ReminderMessageBuilder? messages = null)
    {
        messages ??= ReminderMessageBuilder.English;
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(nowLocal, zone).DateTime);
        List<PlannedReminder> planned = [];
        for (var offset = 0; offset < days; offset++)
        {
            var date = today.AddDays(offset);
            var bit = 1 << (((int)date.DayOfWeek + 6) % 7);
            ReminderDay? day = null;
            foreach (var reminder in reminders)
            {
                if (!reminder.Enabled || (reminder.DaysMask & bit) == 0)
                {
                    continue;
                }

                var fireAt = WallClock(date, reminder.MinutesOfDay, zone);
                if (fireAt <= nowLocal)
                {
                    continue;
                }

                day ??= dayInfo(date);
                if (!day.Completed)
                {
                    planned.Add(new PlannedReminder(reminder.Id, fireAt, messages.Title, messages.Body(day)));
                }
            }
        }

        return [.. planned.OrderBy(p => p.FireAt)];
    }

    /// <summary>The wall-clock time on a date: a skipped spring-forward time moves to the next valid minute, a repeated fall-back time takes its first occurrence.</summary>
    public static DateTimeOffset WallClock(DateOnly date, int minutesOfDay, TimeZoneInfo zone)
    {
        var local = date.ToDateTime(TimeOnly.MinValue).AddMinutes(minutesOfDay);
        while (zone.IsInvalidTime(local))
        {
            local = local.AddMinutes(1);
        }

        var offset = zone.IsAmbiguousTime(local)
            ? zone.GetAmbiguousTimeOffsets(local).Max()
            : zone.GetUtcOffset(local);
        return new DateTimeOffset(local, offset);
    }
}
