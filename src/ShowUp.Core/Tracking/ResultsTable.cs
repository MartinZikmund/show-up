using ShowUp.Core.Catalog;
using ShowUp.Core.Storage;

namespace ShowUp.Core.Tracking;

/// <summary>One time the workout was (or will be) done: a scheduled day of the enrollment, or a bonus workout.</summary>
public sealed record ResultsColumn(DateOnly Date, int? DayIndex, int? SessionIndex, Guid? BonusWorkoutId)
{
    public bool IsBonus => BonusWorkoutId is not null;
}

/// <param name="Delta">Change against the row's first logged value; null for that first value and for empty cells.</param>
/// <param name="Improved">Whether the change is for the better (per <see cref="WorkoutMetric.HigherIsBetter"/>); null when unchanged.</param>
public sealed record ResultsCell(MetricValue? Value, double? Delta, bool? Improved)
{
    public static readonly ResultsCell Empty = new(null, null, null);

    public bool IsEmpty => Value is null;
}

public sealed record ResultsRow(WorkoutMetric Metric, IReadOnlyList<ResultsCell> Cells);

/// <summary>Every logged result of one workout across an enrollment: rows are its metrics, columns its occurrences by day.</summary>
public sealed record ResultsTable(Workout Workout, IReadOnlyList<ResultsColumn> Columns, IReadOnlyList<ResultsRow> Rows)
{
    public bool HasValues => Rows.Any(r => r.Cells.Any(c => !c.IsEmpty));

    /// <summary>
    /// A table per workout with metrics, in order of first appearance. Scheduled occurrences are always columns (empty until
    /// logged); a bonus workout is a column only when it has a value.
    /// </summary>
    /// <param name="dateOf">The enrollment calendar's date for a day index.</param>
    public static IReadOnlyList<ResultsTable> Build(
        IReadOnlyList<ScheduleDay> days,
        Func<int, DateOnly> dateOf,
        IReadOnlyDictionary<string, Workout> workouts,
        IEnumerable<MetricEntryEntity> sessionEntries,
        IEnumerable<BonusWorkoutEntity> bonuses,
        IEnumerable<MetricEntryEntity> bonusEntries)
    {
        ArgumentNullException.ThrowIfNull(days);
        ArgumentNullException.ThrowIfNull(dateOf);
        ArgumentNullException.ThrowIfNull(workouts);

        var sessionValues = sessionEntries
            .Where(e => e.DayIndex is not null && e.SessionIndex is not null)
            .DistinctBy(e => (e.DayIndex, e.SessionIndex, e.MetricId))
            .ToDictionary(e => (e.DayIndex!.Value, e.SessionIndex!.Value, e.MetricId), MetricValue.Of);
        var bonusValues = bonusEntries
            .Where(e => e.BonusWorkoutId is not null)
            .DistinctBy(e => (e.BonusWorkoutId, e.MetricId))
            .ToDictionary(e => (e.BonusWorkoutId!.Value, e.MetricId), MetricValue.Of);
        var loggedBonuses = bonusValues.Keys.Select(k => k.Item1).ToHashSet();

        List<(Workout Workout, ResultsColumn Column)> occurrences = [];
        for (var d = 0; d < days.Count; d++)
        {
            var sessions = days[d].Sessions;
            for (var s = 0; s < sessions.Count; s++)
            {
                if (workouts.TryGetValue(sessions[s].WorkoutId, out var workout) && workout.HasMetrics)
                {
                    occurrences.Add((workout, new ResultsColumn(dateOf(d), d, s, null)));
                }
            }
        }

        foreach (var bonus in bonuses.Where(b => loggedBonuses.Contains(b.Id)))
        {
            if (bonus.WorkoutId is { } id && workouts.TryGetValue(id, out var workout) && workout.HasMetrics)
            {
                occurrences.Add((workout, new ResultsColumn(bonus.Date, null, null, bonus.Id)));
            }
        }

        return
        [
            .. occurrences
                .GroupBy(o => o.Workout.Id)
                .Select(g =>
                {
                    var workout = g.First().Workout;
                    List<ResultsColumn> columns =
                    [
                        .. g.Select(o => o.Column)
                            .OrderBy(c => c.Date)
                            .ThenBy(c => c.IsBonus)
                            .ThenBy(c => c.DayIndex)
                            .ThenBy(c => c.SessionIndex),
                    ];
                    List<ResultsRow> rows =
                    [
                        .. workout.Metrics!.Select(metric => BuildRow(metric, columns, column => column switch
                        {
                            { BonusWorkoutId: { } bonusId } => bonusValues.TryGetValue((bonusId, metric.Id), out var v) ? v : null,
                            { DayIndex: { } day, SessionIndex: { } session } => sessionValues.TryGetValue((day, session, metric.Id), out var v) ? v : null,
                            _ => null,
                        })),
                    ];
                    return new ResultsTable(workout, columns, rows);
                }),
        ];
    }

    private static ResultsRow BuildRow(WorkoutMetric metric, IReadOnlyList<ResultsColumn> columns, Func<ResultsColumn, MetricValue?> valueOf)
    {
        double? first = null;
        List<ResultsCell> cells = [];
        foreach (var column in columns)
        {
            if (valueOf(column) is not { IsEmpty: false } value)
            {
                cells.Add(ResultsCell.Empty);
                continue;
            }

            if (value.Number is not { } number || first is not { } baseline)
            {
                first ??= value.Number;
                cells.Add(new ResultsCell(value, null, null));
                continue;
            }

            var delta = number - baseline;
            bool? improved = delta == 0 ? null : (delta > 0) == metric.HigherIsBetter;
            cells.Add(new ResultsCell(value, delta, improved));
        }

        return new ResultsRow(metric, cells);
    }
}
