using CommunityToolkit.Mvvm.Messaging;
using ShowUp.Core.Catalog;
using ShowUp.Core.Messages;
using ShowUp.Core.Storage;

namespace ShowUp.Core.Tracking;

public sealed record SessionJournal(
    int SessionIndex,
    string Name,
    bool IsDone,
    bool IsOptional,
    string Note,
    ResultsSheet? Results);

public sealed record BonusJournal(BonusWorkoutEntity Bonus, ResultsSheet? Results);

public sealed record DayJournal(int DayIndex, DateOnly Date, IReadOnlyList<SessionJournal> Sessions, string Note, IReadOnlyList<BonusJournal> Bonuses);

/// <summary>A workout's metrics with what is logged so far, for one scheduled session or bonus workout.</summary>
public sealed record ResultsSheet(Workout Workout, MetricTarget Target, DateOnly Date, int? DayNumber, IReadOnlyDictionary<string, MetricValue> Values);

/// <summary>
/// Notes and logged results of the active enrollment, and results of bonus workouts. Crossing stays with
/// <see cref="IActiveEnrollmentService"/>; this is the only path that writes notes and results.
/// </summary>
public interface IJournalService
{
    Task<string> GetProgramNoteAsync();

    /// <summary>Blank text deletes the note. Throws <see cref="InvalidOperationException"/> without an enrollment.</summary>
    Task SaveProgramNoteAsync(string? text);

    Task<DayJournal?> GetDayAsync(int dayIndex);

    /// <param name="sessionIndex">Null for the day's own note.</param>
    Task SaveNoteAsync(int dayIndex, int? sessionIndex, string? text);

    /// <summary>Null when the session's workout has nothing to log.</summary>
    Task<ResultsSheet?> GetResultsSheetAsync(int dayIndex, int sessionIndex);

    Task<ResultsSheet?> GetBonusResultsSheetAsync(BonusWorkoutEntity bonus);

    /// <summary>Writes every value given; an empty one clears it.</summary>
    Task SaveResultsAsync(MetricTarget target, IReadOnlyDictionary<string, MetricValue> values);

    /// <summary>A table per workout with metrics in the active enrollment, bonus workouts within its dates included.</summary>
    Task<IReadOnlyList<ResultsTable>> GetResultsAsync();
}

public sealed class JournalService(
    IActiveEnrollmentService enrollments,
    ICatalogService catalog,
    INotesRepository notes,
    IMetricsRepository metrics,
    IMessenger messenger) : IJournalService
{
    public async Task<string> GetProgramNoteAsync()
    {
        if (await enrollments.GetAsync() is not { } active)
        {
            return "";
        }

        var all = await notes.GetNotesAsync(active.Enrollment.Id);
        return all.FirstOrDefault(n => n.DayIndex is null)?.Text ?? "";
    }

    public async Task SaveProgramNoteAsync(string? text)
    {
        var active = await RequireAsync();
        await notes.SetNoteAsync(active.Enrollment.Id, null, null, text);
        NotifyChanged();
    }

    public async Task<DayJournal?> GetDayAsync(int dayIndex)
    {
        if (await enrollments.GetAsync() is not { } active || dayIndex < 0 || dayIndex >= active.Calendar.DayCount)
        {
            return null;
        }

        var workouts = (await catalog.GetCatalogAsync()).WorkoutsById;
        var day = catalog.GetTrackDays(active.Track)[dayIndex];
        var date = active.Calendar.DateOf(dayIndex);
        var dayNotes = (await notes.GetNotesAsync(active.Enrollment.Id)).Where(n => n.DayIndex == dayIndex).ToList();
        var done = (await enrollments.GetCompletionsAsync()).Where(c => c.DayIndex == dayIndex).Select(c => c.SessionIndex).ToHashSet();
        var entries = (await metrics.GetSessionEntriesAsync(active.Enrollment.Id)).Where(e => e.DayIndex == dayIndex).ToList();

        List<SessionJournal> sessions = [];
        for (var s = 0; s < day.Sessions.Count; s++)
        {
            var session = day.Sessions[s];
            var workout = workouts.GetValueOrDefault(session.WorkoutId);
            var target = MetricTarget.ForSession(active.Enrollment.Id, dayIndex, s, session.WorkoutId);
            sessions.Add(new SessionJournal(
                s,
                workout?.Name ?? session.WorkoutId,
                done.Contains(s),
                session.Optional,
                dayNotes.FirstOrDefault(n => n.SessionIndex == s)?.Text ?? "",
                workout is { HasMetrics: true } ? Sheet(workout, target, date, dayIndex + 1, entries) : null));
        }

        var bonuses = await enrollments.GetBonusWorkoutsAsync(date, date);
        var bonusEntries = await metrics.GetBonusEntriesAsync([.. bonuses.Select(b => b.Id)]);
        List<BonusJournal> bonusJournals =
        [
            .. bonuses.Select(b => new BonusJournal(b, BonusSheet(b, workouts, bonusEntries))),
        ];

        return new DayJournal(dayIndex, date, sessions, dayNotes.FirstOrDefault(n => n.SessionIndex is null)?.Text ?? "", bonusJournals);
    }

    public async Task SaveNoteAsync(int dayIndex, int? sessionIndex, string? text)
    {
        var active = await RequireAsync();
        ArgumentOutOfRangeException.ThrowIfNegative(dayIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(dayIndex, active.Calendar.DayCount);
        await notes.SetNoteAsync(active.Enrollment.Id, dayIndex, sessionIndex, text);
        NotifyChanged();
    }

    public async Task<ResultsSheet?> GetResultsSheetAsync(int dayIndex, int sessionIndex)
    {
        if (await enrollments.GetAsync() is not { } active || dayIndex < 0 || dayIndex >= active.Calendar.DayCount)
        {
            return null;
        }

        var sessions = catalog.GetTrackDays(active.Track)[dayIndex].Sessions;
        if (sessionIndex < 0 || sessionIndex >= sessions.Count)
        {
            return null;
        }

        var workoutId = sessions[sessionIndex].WorkoutId;
        if ((await catalog.GetCatalogAsync()).WorkoutsById.GetValueOrDefault(workoutId) is not { HasMetrics: true } workout)
        {
            return null;
        }

        var entries = (await metrics.GetSessionEntriesAsync(active.Enrollment.Id)).Where(e => e.DayIndex == dayIndex).ToList();
        return Sheet(workout, MetricTarget.ForSession(active.Enrollment.Id, dayIndex, sessionIndex, workoutId), active.Calendar.DateOf(dayIndex), dayIndex + 1, entries);
    }

    public async Task<ResultsSheet?> GetBonusResultsSheetAsync(BonusWorkoutEntity bonus)
    {
        ArgumentNullException.ThrowIfNull(bonus);
        var workouts = (await catalog.GetCatalogAsync()).WorkoutsById;
        return BonusSheet(bonus, workouts, await metrics.GetBonusEntriesAsync([bonus.Id]));
    }

    public async Task SaveResultsAsync(MetricTarget target, IReadOnlyDictionary<string, MetricValue> values)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(values);
        if (!target.IsBonus)
        {
            var active = await RequireAsync();
            if (target.EnrollmentId != active.Enrollment.Id)
            {
                throw new InvalidOperationException("The results belong to an enrollment that is no longer active.");
            }
        }

        foreach (var (metricId, value) in values)
        {
            await metrics.SetValueAsync(target, metricId, value);
        }

        NotifyChanged();
    }

    public async Task<IReadOnlyList<ResultsTable>> GetResultsAsync()
    {
        if (await enrollments.GetAsync() is not { } active)
        {
            return [];
        }

        var workouts = (await catalog.GetCatalogAsync()).WorkoutsById;
        var bonuses = await enrollments.GetBonusWorkoutsAsync();
        return ResultsTable.Build(
            catalog.GetTrackDays(active.Track),
            active.Calendar.DateOf,
            workouts,
            await metrics.GetSessionEntriesAsync(active.Enrollment.Id),
            bonuses,
            await metrics.GetBonusEntriesAsync([.. bonuses.Select(b => b.Id)]));
    }

    private static ResultsSheet? BonusSheet(BonusWorkoutEntity bonus, IReadOnlyDictionary<string, Workout> workouts, IReadOnlyList<MetricEntryEntity> entries) =>
        bonus.WorkoutId is { } id && workouts.GetValueOrDefault(id) is { HasMetrics: true } workout
            ? Sheet(workout, MetricTarget.ForBonus(bonus.Id, id), bonus.Date, null, entries)
            : null;

    private static ResultsSheet Sheet(Workout workout, MetricTarget target, DateOnly date, int? dayNumber, IEnumerable<MetricEntryEntity> entries)
    {
        Dictionary<string, MetricValue> values = [];
        foreach (var entry in entries.Where(target.Matches))
        {
            values.TryAdd(entry.MetricId, MetricValue.Of(entry));
        }

        return new ResultsSheet(workout, target, date, dayNumber, values);
    }

    private async Task<ActiveEnrollment> RequireAsync() =>
        await enrollments.GetAsync() ?? throw new InvalidOperationException("There is no active enrollment.");

    private void NotifyChanged() => messenger.Send(new JournalChangedMessage());
}
