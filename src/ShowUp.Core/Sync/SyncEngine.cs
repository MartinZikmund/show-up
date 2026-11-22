using Microsoft.Extensions.Logging;
using ShowUp.Core.Storage;
using ShowUp.Core.Supabase;

namespace ShowUp.Core.Sync;

public sealed record SyncResult(int Pushed, int Applied);

public sealed class SyncProgress
{
    public int Pushed { get; set; }

    public int Applied { get; set; }
}

/// <summary>One sync pass for one account: per table, push the pending rows, then pull what changed on the server (docs/sync.md).</summary>
public sealed class SyncEngine
{
    public const int PushBatchSize = 200;
    public const int PullPageSize = 500;
    public static readonly TimeSpan PullOverlap = TimeSpan.FromSeconds(5);

    private readonly ShowUpDatabase _db;
    private readonly ISupabaseRest _rest;

    public SyncEngine(ShowUpDatabase db, ISupabaseRest rest, ILogger<SyncEngine> logger)
    {
        _db = db;
        _rest = rest;
        Tables = CreateTables(logger);
    }

    public IReadOnlyList<ISyncTable> Tables { get; }

    /// <param name="progress">Counts as it goes, so a sync that fails halfway still tells what it applied.</param>
    public async Task<SyncResult> SyncAsync(string userId, SyncProgress? progress = null, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(userId);
        progress ??= new();
        var connection = await _db.GetConnectionAsync();
        foreach (var table in Tables)
        {
            var push = await table.PushAsync(connection, _rest, userId, ct);
            progress.Pushed += push.Pushed;
            progress.Applied += push.Applied;
            progress.Applied += await table.PullAsync(connection, _rest, userId, ct);
        }

        return new(progress.Pushed, progress.Applied);
    }

    /// <summary>Rows of <paramref name="userId"/> changed here that the server hasn't confirmed yet.</summary>
    public async Task<int> CountPendingAsync(string userId)
    {
        var connection = await _db.GetConnectionAsync();
        int count = 0;
        foreach (var table in Tables)
        {
            count += await connection.ExecuteScalarAsync<int>($"""SELECT COUNT(*) FROM "{table.LocalName}" WHERE "UserId" = ? AND "SyncPending" = 1""", userId);
        }

        return count;
    }

    private static List<ISyncTable> CreateTables(ILogger logger) =>
    [
        new SyncTable<EnrollmentEntity, EnrollmentRow>(
            "enrollments",
            SyncJsonContext.Default.EnrollmentRow,
            (e, r) =>
            {
                r.ProgramId = e.ProgramId;
                r.ProgramRevision = e.ProgramRevision;
                r.TrackId = e.TrackId;
                r.StartDayNumber = e.StartDayNumber;
                r.EndedAt = e.EndedAt;
                r.EndReason = e.EndReason;
            },
            (r, e) =>
            {
                e.ProgramId = r.ProgramId;
                e.ProgramRevision = r.ProgramRevision;
                e.TrackId = r.TrackId;
                e.StartDayNumber = r.StartDayNumber;
                e.EndedAt = r.EndedAt;
                e.EndReason = r.EndReason;
            },
            logger),
        new SyncTable<CompletionEntity, CompletionRow>(
            "completions",
            SyncJsonContext.Default.CompletionRow,
            (e, r) =>
            {
                r.EnrollmentId = e.EnrollmentId;
                r.DayIndex = e.DayIndex;
                r.SessionIndex = e.SessionIndex;
                r.CompletedOnDayNumber = e.CompletedOnDayNumber;
                r.CompletedAt = e.CompletedAt;
                r.InkJson = e.InkJson;
            },
            (r, e) =>
            {
                e.EnrollmentId = r.EnrollmentId;
                e.DayIndex = r.DayIndex;
                e.SessionIndex = r.SessionIndex;
                e.CompletedOnDayNumber = r.CompletedOnDayNumber;
                e.CompletedAt = r.CompletedAt;
                e.InkJson = r.InkJson;
            },
            logger),
        new SyncTable<ScheduleShiftEntity, ScheduleShiftRow>(
            "schedule_shifts",
            SyncJsonContext.Default.ScheduleShiftRow,
            (e, r) =>
            {
                r.EnrollmentId = e.EnrollmentId;
                r.FromDayNumber = e.FromDayNumber;
                r.Days = e.Days;
            },
            (r, e) =>
            {
                e.EnrollmentId = r.EnrollmentId;
                e.FromDayNumber = r.FromDayNumber;
                e.Days = r.Days;
            },
            logger),
        new SyncTable<BonusWorkoutEntity, BonusWorkoutRow>(
            "bonus_workouts",
            SyncJsonContext.Default.BonusWorkoutRow,
            (e, r) =>
            {
                r.WorkoutId = e.WorkoutId;
                r.Title = e.Title;
                r.DayNumber = e.DayNumber;
                r.DurationMinutes = e.DurationMinutes;
            },
            (r, e) =>
            {
                e.WorkoutId = r.WorkoutId;
                e.Title = r.Title;
                e.DayNumber = r.DayNumber;
                e.DurationMinutes = r.DurationMinutes;
            },
            logger),
        new SyncTable<NoteEntity, NoteRow>(
            "notes",
            SyncJsonContext.Default.NoteRow,
            (e, r) =>
            {
                r.EnrollmentId = e.EnrollmentId;
                r.DayIndex = e.DayIndex;
                r.SessionIndex = e.SessionIndex;
                r.SubjectKey = e.SubjectKey;
                r.Text = e.Text;
            },
            (r, e) =>
            {
                e.EnrollmentId = r.EnrollmentId;
                e.DayIndex = r.DayIndex;
                e.SessionIndex = r.SessionIndex;
                e.SubjectKey = r.SubjectKey;
                e.Text = r.Text;
            },
            logger),
        new SyncTable<MetricEntryEntity, MetricEntryRow>(
            "metric_entries",
            SyncJsonContext.Default.MetricEntryRow,
            (e, r) =>
            {
                r.EnrollmentId = e.EnrollmentId;
                r.DayIndex = e.DayIndex;
                r.SessionIndex = e.SessionIndex;
                r.BonusWorkoutId = e.BonusWorkoutId;
                r.SubjectKey = e.SubjectKey;
                r.WorkoutId = e.WorkoutId;
                r.MetricId = e.MetricId;
                r.Value = e.Value;
                r.TextValue = e.TextValue;
            },
            (r, e) =>
            {
                e.EnrollmentId = r.EnrollmentId;
                e.DayIndex = r.DayIndex;
                e.SessionIndex = r.SessionIndex;
                e.BonusWorkoutId = r.BonusWorkoutId;
                e.SubjectKey = r.SubjectKey;
                e.WorkoutId = r.WorkoutId;
                e.MetricId = r.MetricId;
                e.Value = r.Value;
                e.TextValue = r.TextValue;
            },
            logger),
        new SyncTable<ReminderEntity, ReminderRow>(
            "reminders",
            SyncJsonContext.Default.ReminderRow,
            (e, r) =>
            {
                r.MinutesOfDay = e.MinutesOfDay;
                r.DaysMask = e.DaysMask;
                r.Enabled = e.Enabled;
            },
            (r, e) =>
            {
                e.MinutesOfDay = r.MinutesOfDay;
                e.DaysMask = r.DaysMask;
                e.Enabled = r.Enabled;
            },
            logger),
        new SyncTable<CustomProgramEntity, CustomProgramRow>(
            "custom_programs",
            SyncJsonContext.Default.CustomProgramRow,
            (e, r) =>
            {
                r.SubjectKey = e.SubjectKey;
                r.Revision = e.Revision;
                r.ProgramJson = e.ProgramJson;
            },
            (r, e) =>
            {
                e.SubjectKey = r.SubjectKey;
                e.Revision = r.Revision;
                e.ProgramJson = r.ProgramJson;
            },
            logger),
        new SyncTable<SyncedSettingEntity, SyncedSettingRow>(
            "synced_settings",
            SyncJsonContext.Default.SyncedSettingRow,
            (e, r) =>
            {
                r.SubjectKey = e.SubjectKey;
                r.Key = e.Key;
                r.Value = e.Value;
            },
            (r, e) =>
            {
                e.SubjectKey = r.SubjectKey;
                e.Key = r.Key;
                e.Value = r.Value;
            },
            logger),
    ];
}
