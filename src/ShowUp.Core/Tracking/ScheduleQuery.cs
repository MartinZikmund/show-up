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
}

public sealed class ScheduleQuery(IActiveEnrollmentService enrollments, ICatalogService catalog) : IScheduleQuery
{
    public async Task<IReadOnlyList<DueProgram>> GetDueAsync(DateOnly date)
    {
        var all = await enrollments.GetAllAsync();
        if (all.Count == 0)
        {
            return [];
        }

        var selected = await enrollments.GetSelectedAsync();
        var workouts = (await catalog.GetCatalogAsync()).WorkoutsById;
        List<DueProgram> due = [];
        foreach (var active in all.OrderBy(e => e.Id == selected?.Id ? 0 : 1))
        {
            var completions = await enrollments.GetCompletionsAsync(active.Id);
            var statuses = ProgressCalculator.Build(
                active.Track,
                workouts,
                active.Calendar,
                completions.Select(c => (c.DayIndex, c.SessionIndex)).ToHashSet());
            due.Add(new DueProgram(active, statuses, completions, active.Calendar.DayIndexOn(date), enrollments.IsReadOnly(active)));
        }

        return due;
    }
}
