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

public sealed record ResultsEdit(MetricTarget Target, IReadOnlyDictionary<string, MetricValue> Values);

/// <summary>Everything the day sheet changed; <paramref name="DayNote"/> null means unchanged.</summary>
public sealed record DayEdit(string? DayNote, IReadOnlyDictionary<int, string> SessionNotes, IReadOnlyList<ResultsEdit> Results)
{
    public bool IsEmpty => DayNote is null && SessionNotes.Count == 0 && Results.Count == 0;
}

/// <summary>
/// Notes and logged results of an active enrollment, and results of bonus workouts. Crossing stays with
/// <see cref="IActiveEnrollmentService"/>; this is the only path that writes notes and results. Writes to a
/// read-only enrollment throw <see cref="EnrollmentReadOnlyException"/>.
/// </summary>
public interface IJournalService
{
    Task<string> GetProgramNoteAsync(Guid enrollmentId);

    /// <summary>Blank text deletes the note. Throws <see cref="InvalidOperationException"/> when it isn't active.</summary>
    Task SaveProgramNoteAsync(Guid enrollmentId, string? text);

    Task<DayJournal?> GetDayAsync(Guid enrollmentId, int dayIndex);

    /// <summary>Writes the day's notes and results, then sends one <see cref="JournalChangedMessage"/>.</summary>
    Task SaveDayAsync(Guid enrollmentId, int dayIndex, DayEdit edit);

    /// <summary>Whether the session's workout has anything to log (cheap: no stored values are read).</summary>
    Task<bool> HasMetricsAsync(Guid enrollmentId, int dayIndex, int sessionIndex);

    /// <summary>Null when the session's workout has nothing to log.</summary>
    Task<ResultsSheet?> GetResultsSheetAsync(Guid enrollmentId, int dayIndex, int sessionIndex);

    Task<ResultsSheet?> GetBonusResultsSheetAsync(BonusWorkoutEntity bonus);

    /// <summary>
    /// Writes every value given in one transaction; an empty one clears it. Throws <see cref="ArgumentException"/> for a metric
    /// the workout doesn't have or a session whose scheduled workout isn't <see cref="MetricTarget.WorkoutId"/>.
    /// </summary>
    Task SaveResultsAsync(MetricTarget target, IReadOnlyDictionary<string, MetricValue> values);

    /// <summary>
    /// A table per workout with metrics in the enrollment, with bonus workouts from a week before its start to its end.
    /// </summary>
    Task<IReadOnlyList<ResultsTable>> GetResultsAsync(Guid enrollmentId);
}

public sealed class JournalService(
    IActiveEnrollmentService enrollments,
    ICatalogService catalog,
    INotesRepository notes,
    IMetricsRepository metrics,
    IMessenger messenger) : IJournalService
{
    /// <summary>A Fit Test taken as a bonus in the days before Day 1 still counts as the baseline.</summary>
    public const int BonusDaysBeforeStart = 7;

    public async Task<string> GetProgramNoteAsync(Guid enrollmentId)
    {
        if (await enrollments.GetAsync(enrollmentId) is not { } active)
        {
            return "";
        }

        var all = await notes.GetNotesAsync(active.Enrollment.Id);
        return all.FirstOrDefault(n => n.DayIndex is null)?.Text ?? "";
    }

    public async Task SaveProgramNoteAsync(Guid enrollmentId, string? text)
    {
        var active = await RequireWritableAsync(enrollmentId);
        await notes.SetNotesAsync(active.Enrollment.Id, [new NoteEdit(null, null, text)]);
        NotifyChanged();
    }

    public async Task<DayJournal?> GetDayAsync(Guid enrollmentId, int dayIndex)
    {
        if (await enrollments.GetAsync(enrollmentId) is not { } active || dayIndex < 0 || dayIndex >= active.Calendar.DayCount)
        {
            return null;
        }

        var workouts = (await catalog.GetCatalogAsync()).WorkoutsById;
        var day = catalog.GetTrackDays(active.Track)[dayIndex];
        var date = active.Calendar.DateOf(dayIndex);
        var dayNotes = (await notes.GetNotesAsync(active.Enrollment.Id)).Where(n => n.DayIndex == dayIndex).ToList();
        var done = (await enrollments.GetCompletionsAsync(active.Id)).Where(c => c.DayIndex == dayIndex).Select(c => c.SessionIndex).ToHashSet();
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

    public async Task SaveDayAsync(Guid enrollmentId, int dayIndex, DayEdit edit)
    {
        ArgumentNullException.ThrowIfNull(edit);
        var active = await RequireWritableAsync(enrollmentId);
        ArgumentOutOfRangeException.ThrowIfNegative(dayIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(dayIndex, active.Calendar.DayCount);
        if (edit.IsEmpty)
        {
            return;
        }

        List<NoteEdit> noteEdits = [.. edit.SessionNotes.Select(n => new NoteEdit(dayIndex, n.Key, n.Value))];
        if (edit.DayNote is not null)
        {
            noteEdits.Add(new NoteEdit(dayIndex, null, edit.DayNote));
        }

        foreach (var results in edit.Results)
        {
            await ValidateAsync(active, results.Target, results.Values);
        }

        if (noteEdits.Count > 0)
        {
            await notes.SetNotesAsync(active.Enrollment.Id, noteEdits);
        }

        foreach (var results in edit.Results)
        {
            await metrics.SetValuesAsync(results.Target, results.Values);
        }

        NotifyChanged();
    }

    public async Task<bool> HasMetricsAsync(Guid enrollmentId, int dayIndex, int sessionIndex) =>
        await ScheduledWorkoutAsync(enrollmentId, dayIndex, sessionIndex) is { Workout.HasMetrics: true };

    public async Task<ResultsSheet?> GetResultsSheetAsync(Guid enrollmentId, int dayIndex, int sessionIndex)
    {
        if (await ScheduledWorkoutAsync(enrollmentId, dayIndex, sessionIndex) is not { Workout: { HasMetrics: true } workout } scheduled)
        {
            return null;
        }

        var entries = (await metrics.GetSessionEntriesAsync(enrollmentId)).Where(e => e.DayIndex == dayIndex).ToList();
        return Sheet(
            workout,
            MetricTarget.ForSession(enrollmentId, dayIndex, sessionIndex, workout.Id),
            scheduled.Active.Calendar.DateOf(dayIndex),
            dayIndex + 1,
            entries);
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
        await ValidateAsync(target.IsBonus ? null : await RequireWritableAsync(target.EnrollmentId!.Value), target, values);
        await metrics.SetValuesAsync(target, values);
        NotifyChanged();
    }

    public async Task<IReadOnlyList<ResultsTable>> GetResultsAsync(Guid enrollmentId)
    {
        if (await enrollments.GetAsync(enrollmentId) is not { } active)
        {
            return [];
        }

        var workouts = (await catalog.GetCatalogAsync()).WorkoutsById;
        var bonuses = await enrollments.GetBonusWorkoutsAsync(active.Calendar.DateOf(0).AddDays(-BonusDaysBeforeStart), active.Calendar.EndDate);
        return ResultsTable.Build(
            catalog.GetTrackDays(active.Track),
            active.Calendar.DateOf,
            workouts,
            await metrics.GetSessionEntriesAsync(active.Enrollment.Id),
            bonuses,
            await metrics.GetBonusEntriesAsync([.. bonuses.Select(b => b.Id)]));
    }

    private async Task ValidateAsync(ActiveEnrollment? active, MetricTarget target, IReadOnlyDictionary<string, MetricValue> values)
    {
        if (!target.IsBonus)
        {
            if (active is null || target.EnrollmentId != active.Enrollment.Id)
            {
                throw new InvalidOperationException("The results belong to an enrollment that is no longer active.");
            }

            var scheduled = await ScheduledWorkoutAsync(active.Id, target.DayIndex!.Value, target.SessionIndex!.Value);
            if (scheduled?.Workout.Id != target.WorkoutId)
            {
                throw new ArgumentException($"Day {target.DayIndex + 1}, session {target.SessionIndex} is not '{target.WorkoutId}'.", nameof(target));
            }
        }

        var workout = (await catalog.GetCatalogAsync()).WorkoutsById.GetValueOrDefault(target.WorkoutId);
        var known = (workout?.Metrics ?? []).Select(m => m.Id).ToHashSet();
        if (values.Keys.FirstOrDefault(id => !known.Contains(id)) is { } unknown)
        {
            throw new ArgumentException($"'{target.WorkoutId}' has no metric '{unknown}'.", nameof(values));
        }
    }

    private async Task<(ActiveEnrollment Active, Workout Workout)?> ScheduledWorkoutAsync(Guid enrollmentId, int dayIndex, int sessionIndex)
    {
        if (await enrollments.GetAsync(enrollmentId) is not { } active || dayIndex < 0 || dayIndex >= active.Calendar.DayCount)
        {
            return null;
        }

        var sessions = catalog.GetTrackDays(active.Track)[dayIndex].Sessions;
        if (sessionIndex < 0 || sessionIndex >= sessions.Count)
        {
            return null;
        }

        return (await catalog.GetCatalogAsync()).WorkoutsById.GetValueOrDefault(sessions[sessionIndex].WorkoutId) is { } workout
            ? (active, workout)
            : null;
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

    private async Task<ActiveEnrollment> RequireWritableAsync(Guid enrollmentId)
    {
        var active = await enrollments.GetAsync(enrollmentId) ?? throw new InvalidOperationException("The enrollment is no longer active.");
        return enrollments.IsReadOnly(active) ? throw new EnrollmentReadOnlyException(enrollmentId) : active;
    }

    private void NotifyChanged() => messenger.Send(new JournalChangedMessage());
}
