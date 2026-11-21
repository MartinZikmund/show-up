using ShowUp.Core.Catalog;

namespace ShowUp.Core.Tracking;

/// <summary>One active enrollment as of a date: its day statuses, completions and that date's day, if it has one.</summary>
public sealed record DueProgram(
    ActiveEnrollment Active,
    IReadOnlyList<DayStatus> Statuses,
    IReadOnlyList<CompletedSession> Completions,
    int? DayIndex,
    bool IsReadOnly)
{
    public DayStatus? Day => DayIndex is { } i ? Statuses[i] : null;

    public IReadOnlyList<CompletedSession> DayCompletions => DayIndex is { } i ? [.. Completions.Where(c => c.DayIndex == i)] : [];

    /// <summary>A required session on the day is still open.</summary>
    public bool HasOpenSessions => Day is { CountsAsRest: false, Progress: not DayProgress.Complete };
}

/// <summary>What is due on a date across every active enrollment; Today and reminders read this.</summary>
public interface IScheduleQuery
{
    /// <summary>One entry per active enrollment, the selected one first; <see cref="DueProgram.DayIndex"/> is null when nothing is scheduled that date.</summary>
    Task<IReadOnlyList<DueProgram>> GetDueAsync(DateOnly date);

    /// <summary>The same for <paramref name="days"/> consecutive dates from <paramref name="from"/>, reading each enrollment once.</summary>
    Task<IReadOnlyDictionary<DateOnly, IReadOnlyList<DueProgram>>> GetDueAsync(DateOnly from, int days);
}

public sealed class ScheduleQuery(IActiveEnrollmentService enrollments, ICatalogService catalog) : IScheduleQuery
{
    public async Task<IReadOnlyList<DueProgram>> GetDueAsync(DateOnly date) => (await GetDueAsync(date, 1))[date];

    public async Task<IReadOnlyDictionary<DateOnly, IReadOnlyList<DueProgram>>> GetDueAsync(DateOnly from, int days)
    {
        Dictionary<DateOnly, IReadOnlyList<DueProgram>> byDate = [];
        var dates = Enumerable.Range(0, days).Select(from.AddDays).ToList();
        var all = await enrollments.GetAllAsync();
        if (all.Count == 0)
        {
            dates.ForEach(d => byDate[d] = []);
            return byDate;
        }

        var selected = await enrollments.GetSelectedAsync();
        var workouts = (await catalog.GetCatalogAsync()).WorkoutsById;
        Dictionary<DateOnly, List<DueProgram>> due = dates.ToDictionary(d => d, _ => new List<DueProgram>());
        foreach (var active in all.OrderBy(e => e.Id == selected?.Id ? 0 : 1))
        {
            var completions = await enrollments.GetCompletionsAsync(active.Id);
            var statuses = ProgressCalculator.Build(
                active.Track,
                workouts,
                active.Calendar,
                completions.Select(c => (c.DayIndex, c.SessionIndex)).ToHashSet());
            var readOnly = enrollments.IsReadOnly(active);
            foreach (var date in dates)
            {
                due[date].Add(new DueProgram(active, statuses, completions, active.Calendar.DayIndexOn(date), readOnly));
            }
        }

        foreach (var (date, list) in due)
        {
            byDate[date] = list;
        }

        return byDate;
    }
}
