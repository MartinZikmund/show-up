using System.Globalization;
using ShowUp.Core.Catalog;
using ShowUp.Core.Localization;
using ShowUp.Core.Tracking;

namespace ShowUp.Core.Reminders;

/// <summary>Reads what each upcoming date asks of the user from <see cref="IScheduleQuery"/>, across all active programs.</summary>
public interface IReminderDayProvider
{
    Task<Func<DateOnly, ReminderDay>> LoadAsync(DateOnly from, int days);
}

public sealed class ReminderDayProvider(IScheduleQuery schedule, ICatalogService catalog, IStringLocalizer localizer) : IReminderDayProvider
{
    public async Task<Func<DateOnly, ReminderDay>> LoadAsync(DateOnly from, int days)
    {
        var workouts = (await catalog.GetCatalogAsync()).WorkoutsById;
        Dictionary<DateOnly, ReminderDay> byDate = [];
        for (var i = 0; i < days; i++)
        {
            var date = from.AddDays(i);
            byDate[date] = Describe(await schedule.GetDueAsync(date), workouts);
        }

        return date => byDate.GetValueOrDefault(date) ?? new ReminderDay(true, null, false);
    }

    private ReminderDay Describe(IReadOnlyList<DueProgram> due, IReadOnlyDictionary<string, Workout> workouts)
    {
        var scheduled = due.Where(d => d.Day is not null).ToList();
        if (scheduled.Count == 0)
        {
            return new ReminderDay(true, null, false);
        }

        List<string> open = [];
        foreach (var program in scheduled)
        {
            var status = program.Day!;
            if (status.CountsAsRest)
            {
                continue;
            }

            var sessions = catalog.GetTrackDays(program.Active.Track)[status.DayIndex].Sessions;
            for (var i = 0; i < sessions.Count; i++)
            {
                if (!sessions[i].Optional && !status.CompletedSessions.Contains(i))
                {
                    open.Add(workouts.TryGetValue(sessions[i].WorkoutId, out var workout) ? workout.Name : sessions[i].WorkoutId);
                }
            }
        }

        if (open.Count > 0)
        {
            var more = open.Count - 1;
            var title = more == 0
                ? open[0]
                : localizer[PluralForms.KeyFor(CultureInfo.CurrentUICulture, "ReminderMore", more), open[0], more].Value;
            return new ReminderDay(false, title, false);
        }

        return scheduled.All(d => d.Day!.CountsAsRest)
            ? new ReminderDay(false, null, true)
            : new ReminderDay(true, null, false);
    }
}
