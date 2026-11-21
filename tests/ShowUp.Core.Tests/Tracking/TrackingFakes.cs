using ShowUp.Core.Catalog;
using ShowUp.Core.Storage;
using ShowUp.Core.Tracking;

namespace ShowUp.Core.Tests.Tracking;

internal static class TestCatalog
{
    public static readonly IReadOnlyList<Workout> Workouts =
    [
        new("run", "Run", 30, WorkoutKind.Cardio, null),
        new("abs", "Abs", 15, WorkoutKind.Abs, null),
        new("rest", "Rest", null, WorkoutKind.Rest, null),
        new("fit", "Fit Test", 30, WorkoutKind.FitTest, null, [new("jacks", "Jacks", MetricUnit.Reps), new("time", "Time", MetricUnit.Duration)]),
    ];

    /// <summary>One phase; each string is a day, "+" joins its sessions and "?" marks one optional (e.g. "run+abs?").</summary>
    public static WorkoutProgram Program(string id, int revision, params string[] days)
    {
        List<ScheduleWeek> weeks = [];
        foreach (var chunk in days.Chunk(7))
        {
            weeks.Add(new ScheduleWeek([.. chunk.Select(d => new ScheduleDay([.. d.Split('+').Select(w => new ScheduledSession(w.TrimEnd('?'), Optional: w.EndsWith('?')))]))]));
        }

        return new WorkoutProgram(
            id,
            revision,
            $"Program {id} r{revision}",
            "Family",
            "",
            null,
            "#000000",
            VerificationLevel.Official,
            [],
            new DateOnly(2026, 1, 1),
            [new ProgramTrack("t", "Track", [new ProgramPhase("Phase", weeks)])]);
    }
}

internal sealed class FakeCatalogService(params WorkoutProgram[] programs) : ICatalogService
{
    private readonly WorkoutCatalog _catalog = new(1, TestCatalog.Workouts, programs);

    public Task<WorkoutCatalog> GetCatalogAsync() => Task.FromResult(_catalog);

    public Task<WorkoutProgram?> GetProgramAsync(string programId, int? revision = null)
    {
        var matches = programs.Where(p => p.Id == programId);
        return Task.FromResult(revision is { } r
            ? matches.FirstOrDefault(p => p.Revision == r)
            : matches.OrderByDescending(p => p.Revision).FirstOrDefault());
    }

    public Task<Workout?> GetWorkoutAsync(string workoutId) =>
        Task.FromResult(_catalog.WorkoutsById.GetValueOrDefault(workoutId));

    public IReadOnlyList<ScheduleDay> GetTrackDays(ProgramTrack track) => track.Days;
}

internal sealed class FakeTrackingRepository(TimeProvider time) : ITrackingRepository
{
    private readonly List<EnrollmentEntity> _enrollments = [];
    private readonly List<(Guid Enrollment, CompletedSession Completion)> _completions = [];
    private readonly List<(Guid Enrollment, ScheduleShift Shift)> _shifts = [];
    private readonly List<BonusWorkoutEntity> _bonuses = [];

    public List<(int Day, int Session, DateOnly Date, string? Ink)> Crosses { get; } = [];

    public List<(Guid Enrollment, int Day, int Session)> CrossesByEnrollment { get; } = [];

    public List<(DateOnly From, int Days, int DayCount)> ShiftCalls { get; } = [];

    /// <summary>Shifts throw like the real repository does when every remaining day is crossed.</summary>
    public bool FailShifts { get; set; }

    /// <summary>When set, crosses wait for it, so a test can tap again while a write is in flight.</summary>
    public TaskCompletionSource? CrossGate { get; set; }

    private DateOnly Today => DateOnly.FromDateTime(time.GetLocalNow().DateTime);

    /// <summary>The next enroll or replace throws, the way a failing insert does.</summary>
    public bool FailEnroll { get; set; }

    public async Task<EnrollmentEntity> ReplaceEnrollmentAsync(Guid endEnrollmentId, EnrollmentEndReason reason, string programId, int revision, string trackId, DateOnly startDate)
    {
        var created = await EnrollAsync(programId, revision, trackId, startDate);
        await EndEnrollmentAsync(endEnrollmentId, reason);
        return created;
    }

    public Task<EnrollmentEntity> EnrollAsync(string programId, int revision, string trackId, DateOnly startDate)
    {
        if (FailEnroll)
        {
            return Task.FromException<EnrollmentEntity>(new InvalidOperationException("Insert failed."));
        }

        EnrollmentEntity entity = new()
        {
            Id = Guid.NewGuid(),
            ProgramId = programId,
            ProgramRevision = revision,
            TrackId = trackId,
            StartDate = startDate,
            CreatedAt = time.GetUtcNow(),
        };
        _enrollments.Add(entity);
        return Task.FromResult(entity);
    }

    public Task<IReadOnlyList<EnrollmentEntity>> GetActiveEnrollmentsAsync() =>
        Task.FromResult<IReadOnlyList<EnrollmentEntity>>([.. _enrollments.Where(e => e.EndedAt is null)]);

    public Task EndEnrollmentAsync(Guid enrollmentId, EnrollmentEndReason reason)
    {
        foreach (var e in _enrollments.Where(e => e.Id == enrollmentId))
        {
            e.EndedAt = time.GetUtcNow();
            e.EndReason = reason;
        }

        return Task.CompletedTask;
    }

    public async Task CrossSessionAsync(Guid enrollmentId, int dayIndex, int sessionIndex, DateOnly dayDate, string? inkJson = null, Guid? completionId = null)
    {
        if (!CrossingPolicy.CanCross(dayDate, Today))
        {
            throw new InvalidOperationException("Future day.");
        }

        if (CrossGate is { } gate)
        {
            await gate.Task;
        }

        Crosses.Add((dayIndex, sessionIndex, dayDate, inkJson));
        CrossesByEnrollment.Add((enrollmentId, dayIndex, sessionIndex));
        if (!_completions.Any(c => c.Enrollment == enrollmentId && c.Completion.DayIndex == dayIndex && c.Completion.SessionIndex == sessionIndex))
        {
            _completions.Add((enrollmentId, new(dayIndex, sessionIndex, completionId ?? Guid.NewGuid(), Today, inkJson, time.GetUtcNow())));
        }
    }

    public Task UncrossSessionAsync(Guid enrollmentId, int dayIndex, int sessionIndex)
    {
        _completions.RemoveAll(c => c.Enrollment == enrollmentId && c.Completion.DayIndex == dayIndex && c.Completion.SessionIndex == sessionIndex);
        return Task.CompletedTask;
    }

    public Task<IReadOnlySet<(int Day, int Session)>> GetCompletedSessionsAsync(Guid enrollmentId) =>
        Task.FromResult<IReadOnlySet<(int Day, int Session)>>(Of(enrollmentId).Select(c => (c.DayIndex, c.SessionIndex)).ToHashSet());

    public Task<IReadOnlyList<CompletedSession>> GetCompletionsAsync(Guid enrollmentId) =>
        Task.FromResult<IReadOnlyList<CompletedSession>>([.. Of(enrollmentId).OrderBy(c => c.DayIndex).ThenBy(c => c.SessionIndex)]);

    public Task<IReadOnlyList<ScheduleShift>> GetShiftsAsync(Guid enrollmentId) =>
        Task.FromResult<IReadOnlyList<ScheduleShift>>([.. _shifts.Where(s => s.Enrollment == enrollmentId).Select(s => s.Shift)]);

    public Task ShiftScheduleAsync(Guid enrollmentId, DateOnly fromDate, int days, int dayCount)
    {
        if (FailShifts)
        {
            throw new InvalidOperationException("Nothing left to shift.");
        }

        ShiftCalls.Add((fromDate, days, dayCount));
        _shifts.Add((enrollmentId, new(fromDate, days)));
        return Task.CompletedTask;
    }

    public Task<BonusWorkoutEntity> LogBonusAsync(string? workoutId, string title, DateOnly date, int? durationMinutes)
    {
        BonusWorkoutEntity entity = new() { Id = Guid.NewGuid(), WorkoutId = workoutId, Title = title, Date = date, DurationMinutes = durationMinutes };
        _bonuses.Add(entity);
        return Task.FromResult(entity);
    }

    public Task<IReadOnlyList<BonusWorkoutEntity>> GetBonusWorkoutsAsync(DateOnly from, DateOnly to) =>
        Task.FromResult<IReadOnlyList<BonusWorkoutEntity>>([.. _bonuses.Where(b => b.Date >= from && b.Date <= to)]);

    public Task RemoveBonusAsync(Guid id)
    {
        _bonuses.RemoveAll(b => b.Id == id);
        return Task.CompletedTask;
    }

    private IEnumerable<CompletedSession> Of(Guid enrollmentId) => _completions.Where(c => c.Enrollment == enrollmentId).Select(c => c.Completion);
}

internal sealed class FakeSyncedSettings : ISyncedSettings
{
    public Dictionary<string, string> Values { get; } = [];

    public Task<string?> GetAsync(string key) => Task.FromResult(Values.GetValueOrDefault(key));

    public Task<IReadOnlyDictionary<string, string>> GetByPrefixAsync(string prefix) =>
        Task.FromResult<IReadOnlyDictionary<string, string>>(Values.Where(v => v.Key.StartsWith(prefix, StringComparison.Ordinal)).ToDictionary());

    public Task SetAsync(string key, string value)
    {
        Values[key] = value;
        return Task.CompletedTask;
    }
}

internal static class TestEnrollments
{
    public static ActiveEnrollmentService Create(
        ITrackingRepository repository,
        ICatalogService catalog,
        CommunityToolkit.Mvvm.Messaging.IMessenger messenger,
        ISyncedSettings? settings = null,
        ShowUp.Core.Entitlements.IEntitlementService? entitlements = null) =>
        new(
            repository,
            catalog,
            settings ?? new FakeSyncedSettings(),
            entitlements ?? new ShowUp.Core.Tests.Entitlements.FakeEntitlementService { IsPro = true },
            messenger,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<ActiveEnrollmentService>.Instance);
}
