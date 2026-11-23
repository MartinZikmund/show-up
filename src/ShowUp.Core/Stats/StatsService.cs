using System.Globalization;
using ShowUp.Core.Catalog;
using ShowUp.Core.Tracking;

namespace ShowUp.Core.Stats;

public interface IStatsService
{
    /// <param name="fullHistory">
    /// False reads only the current program and the last <see cref="StatsService.FreeDays"/> days, so Free never loads what it hides.
    /// </param>
    Task<StatsSummary> GetAsync(bool fullHistory);
}

/// <summary>Collects crossed sessions and bonus workouts into a <see cref="StatsSummary"/>.</summary>
public sealed class StatsService(IActiveEnrollmentService enrollments, ICatalogService catalog, TimeProvider time) : IStatsService
{
    public const int FreeDays = 28;
    public const int FreeWeeks = 4;
    public const int FullWeeks = 12;
    public const int HeatmapWeeks = 26;

    public async Task<StatsSummary> GetAsync(bool fullHistory)
    {
        var today = DateOnly.FromDateTime(time.GetLocalNow().DateTime);
        var from = fullHistory ? DateOnly.MinValue : today.AddDays(1 - FreeDays);
        var workouts = (await catalog.GetCatalogAsync()).WorkoutsById;

        var records = await enrollments.GetHistoryAsync();
        if (!fullHistory)
        {
            // Same "current program" as History: the primary one, else the most recent.
            var primaryId = (await enrollments.GetPrimaryAsync())?.Id;
            var current = records.FirstOrDefault(e => e.Id == primaryId) ?? records.FirstOrDefault();
            records = current is null ? [] : [current];
        }

        List<ActivityEntry> entries = [];
        List<ProgramComparison> programs = [];
        foreach (var record in records)
        {
            var completions = await enrollments.GetHistoryCompletionsAsync(record.Id);
            entries.AddRange(SessionEntries(record, completions, workouts));

            if (fullHistory)
            {
                var statuses = ProgressCalculator.Build(
                    record.Track,
                    workouts,
                    record.Calendar,
                    completions.Select(c => (c.DayIndex, c.SessionIndex)).ToHashSet());
                var through = record.Enrollment.EndedAt is { } ended && LocalDateOf(ended) < today ? LocalDateOf(ended) : today;
                programs.Add(StatsCalculator.Compare(record.Id, record.Program.Name, statuses, through, today));
            }
        }

        foreach (var bonus in await enrollments.GetBonusWorkoutsAsync(from, today))
        {
            var workout = bonus.WorkoutId is { } id ? workouts.GetValueOrDefault(id) : null;
            entries.Add(new ActivityEntry(bonus.Date, bonus.DurationMinutes ?? workout?.DurationMinutes ?? 0, workout?.Kind ?? WorkoutKind.Mixed, IsBonus: true));
        }

        return StatsCalculator.Build(
            [.. entries.Where(e => e.Date >= from && e.Date <= today)],
            programs,
            today,
            CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek,
            fullHistory ? FullWeeks : FreeWeeks,
            fullHistory ? HeatmapWeeks : 0);
    }

    // A session counts on its calendar day, like the X on paper, not on the day the cross was drawn.
    private IEnumerable<ActivityEntry> SessionEntries(ActiveEnrollment record, IReadOnlyList<CompletedSession> completions, IReadOnlyDictionary<string, Workout> workouts)
    {
        var days = catalog.GetTrackDays(record.Track);
        foreach (var completion in completions)
        {
            if (completion.DayIndex < 0 || completion.DayIndex >= days.Count
                || completion.SessionIndex < 0 || completion.SessionIndex >= days[completion.DayIndex].Sessions.Count)
            {
                continue;
            }

            var workout = workouts.GetValueOrDefault(days[completion.DayIndex].Sessions[completion.SessionIndex].WorkoutId);
            if (workout?.Kind == WorkoutKind.Rest)
            {
                continue;
            }

            yield return new ActivityEntry(
                record.Calendar.DateOf(completion.DayIndex),
                workout?.DurationMinutes ?? 0,
                workout?.Kind ?? WorkoutKind.Mixed,
                IsBonus: false);
        }
    }

    private DateOnly LocalDateOf(DateTimeOffset moment) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(moment, time.LocalTimeZone).DateTime);
}
