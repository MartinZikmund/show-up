using ShowUp.Core.Tracking;

namespace ShowUp.Core.Stats;

/// <summary>Turns workouts done into the numbers the statistics page draws. Dates are local calendar days throughout.</summary>
public static class StatsCalculator
{
    /// <param name="entries">Everything to count; totals cover all of it, the bars and the heatmap only their own span.</param>
    /// <param name="weeks">How many week bars, ending with the week <paramref name="today"/> is in.</param>
    /// <param name="heatmapWeeks">How many whole weeks of heatmap; 0 for none.</param>
    public static StatsSummary Build(
        IReadOnlyList<ActivityEntry> entries,
        IReadOnlyList<ProgramComparison> programs,
        DateOnly today,
        DayOfWeek firstDayOfWeek,
        int weeks,
        int heatmapWeeks)
    {
        var currentWeek = WeekStart(today, firstDayOfWeek);
        var byDay = entries.ToLookup(e => e.Date);

        List<WeekBucket> weekBuckets = [];
        for (int back = weeks - 1; back >= 0; back--)
        {
            var start = currentWeek.AddDays(-7 * back);
            var inWeek = Enumerable.Range(0, 7).SelectMany(offset => byDay[start.AddDays(offset)]).ToList();
            weekBuckets.Add(new WeekBucket(start, inWeek.Count, inWeek.Sum(e => e.Minutes)));
        }

        List<HeatDay> heatmap = [];
        var heatStart = currentWeek.AddDays(-7 * (heatmapWeeks - 1));
        for (int offset = 0; offset < heatmapWeeks * 7; offset++)
        {
            var date = heatStart.AddDays(offset);
            heatmap.Add(new HeatDay(date, byDay[date].Count()));
        }

        List<KindBucket> byKind =
        [
            .. entries
                .GroupBy(e => e.Kind)
                .Select(g => new KindBucket(g.Key, g.Count(), g.Sum(e => e.Minutes)))
                .OrderByDescending(k => k.Minutes)
                .ThenByDescending(k => k.Workouts)
                .ThenBy(k => k.Kind),
        ];

        return new StatsSummary(
            entries.Count,
            entries.Sum(e => e.Minutes),
            entries.Select(e => e.Date).Distinct().Count(),
            weekBuckets,
            byKind,
            heatmap,
            programs);
    }

    /// <param name="through">The last day that counts: today for a running program, the day it ended otherwise.</param>
    public static ProgramComparison Compare(Guid enrollmentId, string name, IReadOnlyList<DayStatus> statuses, DateOnly through, DateOnly today)
    {
        var due = statuses.Where(s => s.Date <= through && !s.CountsAsRest).ToList();
        int crossed = due.Count(s => s.Progress == DayProgress.Complete);
        double rate = due.Count == 0 ? 0 : (double)crossed / due.Count;
        return new ProgramComparison(enrollmentId, name, crossed, due.Count, StreakCalculator.Longest(statuses, today), rate);
    }

    private static DateOnly WeekStart(DateOnly date, DayOfWeek firstDayOfWeek) =>
        date.AddDays(-((7 + (int)date.DayOfWeek - (int)firstDayOfWeek) % 7));
}
