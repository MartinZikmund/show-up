using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging;
using ShowUp.Core.Catalog;
using ShowUp.Core.CustomPrograms;
using ShowUp.Core.Entitlements;
using ShowUp.Core.Messages;
using ShowUp.Core.Storage;
using ShowUp.Services.Settings;

namespace ShowUp.Core.Tracking;

public sealed class ActiveEnrollmentService : IActiveEnrollmentService
{
    /// <summary>Synced: the program a lapsed user keeps is the same on every device.</summary>
    public const string PrimaryKey = "primary-enrollment";

    private readonly ITrackingRepository _repository;
    private readonly ICatalogService _catalog;
    private readonly ISyncedSettings _settings;
    private readonly IAppPreferences _preferences;
    private readonly IEntitlementService _entitlements;
    private readonly IMessenger _messenger;
    private readonly ILogger<ActiveEnrollmentService> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    // Replaced whole after each load, so IsReadOnly (synchronous, any thread) never sees a half-updated state.
    private volatile Snapshot _snapshot = new([], null);
    private int _version;
    private int _loadedVersion = -1;

    public ActiveEnrollmentService(
        ITrackingRepository repository,
        ICatalogService catalog,
        ISyncedSettings settings,
        IAppPreferences preferences,
        IEntitlementService entitlements,
        IMessenger messenger,
        ILogger<ActiveEnrollmentService> logger)
    {
        _repository = repository;
        _catalog = catalog;
        _settings = settings;
        _preferences = preferences;
        _entitlements = entitlements;
        _messenger = messenger;
        _logger = logger;

        // Read-only state follows Pro; pages re-read it on the tracking message.
        _entitlements.Changed += (_, _) => _messenger.Send(new TrackingChangedMessage());
        _messenger.Register<ActiveEnrollmentService, DataChangedMessage>(this, static (r, _) => r.Invalidate());
    }

    /// <summary>Stored data changed behind this service's back: the next read loads again.</summary>
    public void Invalidate() => Interlocked.Increment(ref _version);

    /// <summary>Per device (<see cref="IAppPreferences"/>): each device's Calendar can show a different program.</summary>
    public Guid? SelectedEnrollmentId => _preferences.SelectedEnrollmentId;

    public async Task<IReadOnlyList<ActiveEnrollment>> GetAllAsync()
    {
        await _gate.WaitAsync();
        try
        {
            // A write during the load bumps the version, so the next call loads again.
            var version = Volatile.Read(ref _version);
            if (_loadedVersion != version)
            {
                _snapshot = new(await LoadAsync(await _repository.GetActiveEnrollmentsAsync()), ParseId(await _settings.GetAsync(PrimaryKey)));
                _loadedVersion = version;
            }

            return _snapshot.All;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<ActiveEnrollment>> GetHistoryAsync()
    {
        var all = await LoadAsync(await _repository.GetAllEnrollmentsAsync());
        return
        [
            .. all.Where(e => e.Enrollment.EndedAt is null),
            .. all.Where(e => e.Enrollment.EndedAt is not null).OrderByDescending(e => e.Enrollment.EndedAt),
        ];
    }

    public Task<IReadOnlyList<CompletedSession>> GetHistoryCompletionsAsync(Guid enrollmentId) =>
        _repository.GetCompletionsAsync(enrollmentId);

    public async Task<ActiveEnrollment?> GetSelectedAsync()
    {
        var all = await GetAllAsync();
        var selected = SelectedEnrollmentId;
        return all.FirstOrDefault(e => e.Id == selected) ?? all.FirstOrDefault();
    }

    public async Task<ActiveEnrollment?> GetAsync(Guid enrollmentId) =>
        (await GetAllAsync()).FirstOrDefault(e => e.Id == enrollmentId);

    public async Task SelectAsync(Guid enrollmentId)
    {
        if (await GetAsync(enrollmentId) is null)
        {
            throw new InvalidOperationException($"Enrollment {enrollmentId} is not active.");
        }

        _preferences.SelectedEnrollmentId = enrollmentId;
        _messenger.Send(new TrackingChangedMessage());
    }

    public async Task<ActiveEnrollment?> GetPrimaryAsync()
    {
        await GetAllAsync();
        return _snapshot.Primary;
    }

    public async Task<bool> HasPrimaryChoiceAsync()
    {
        await GetAllAsync();
        var snapshot = _snapshot;
        return snapshot.All.Any(e => e.Id == snapshot.PrimaryId);
    }

    public async Task SetPrimaryAsync(Guid enrollmentId)
    {
        if (await GetAsync(enrollmentId) is null)
        {
            throw new InvalidOperationException($"Enrollment {enrollmentId} is not active.");
        }

        await _settings.SetAsync(PrimaryKey, enrollmentId.ToString("D"));
        NotifyChanged();
    }

    public bool IsReadOnly(ActiveEnrollment enrollment)
    {
        ArgumentNullException.ThrowIfNull(enrollment);
        var snapshot = _snapshot;
        return !_entitlements.IsPro
            && snapshot.All.Count > FeatureGate.MaxActivePrograms(isPro: false)
            && snapshot.Primary?.Id != enrollment.Id;
    }

    public async Task<IReadOnlyList<CompletedSession>> GetCompletionsAsync(Guid enrollmentId) =>
        await GetAsync(enrollmentId) is { } active ? await _repository.GetCompletionsAsync(active.Id) : [];

    public async Task<IReadOnlyList<BonusWorkoutEntity>> GetBonusWorkoutsAsync(Guid enrollmentId) =>
        await GetAsync(enrollmentId) is { } active
            ? await _repository.GetBonusWorkoutsAsync(active.Calendar.DateOf(0), active.Calendar.EndDate)
            : [];

    public async Task<ActiveEnrollment> EnrollAsync(WorkoutProgram program, ProgramTrack track, DateOnly startDate)
    {
        ArgumentNullException.ThrowIfNull(program);
        ArgumentNullException.ThrowIfNull(track);
        EnsureStartable(program);

        if ((await GetAllAsync()).Count >= FeatureGate.MaxActivePrograms(_entitlements.IsPro))
        {
            throw new InvalidOperationException("Free runs one program at a time; replace the active one instead.");
        }

        var created = await _repository.EnrollAsync(program.Id, program.Revision, track.Id, startDate);
        _preferences.SelectedEnrollmentId = created.Id;
        NotifyChanged();
        return await GetAsync(created.Id) ?? throw new InvalidOperationException($"Enrollment in '{program.Id}' could not be resolved.");
    }

    public async Task<ActiveEnrollment> ReplaceAsync(Guid enrollmentId, EnrollmentEndReason reason, WorkoutProgram program, ProgramTrack track, DateOnly startDate)
    {
        ArgumentNullException.ThrowIfNull(program);
        ArgumentNullException.ThrowIfNull(track);
        EnsureStartable(program);

        // A read-only program stays as it was until Pro is back; restarting it would edit it.
        var old = await RequireWritableAsync(enrollmentId);
        var wasPrimary = _snapshot.PrimaryId == old.Id;
        EnrollmentEntity created;
        try
        {
            created = await _repository.ReplaceEnrollmentAsync(old.Id, reason, program.Id, program.Revision, track.Id, startDate);
        }
        catch
        {
            NotifyChanged();
            throw;
        }

        _preferences.SelectedEnrollmentId = created.Id;
        if (wasPrimary)
        {
            await _settings.SetAsync(PrimaryKey, created.Id.ToString("D"));
        }

        NotifyChanged();
        return await GetAsync(created.Id) ?? throw new InvalidOperationException($"Enrollment in '{program.Id}' could not be resolved.");
    }

    public async Task EndAsync(Guid enrollmentId, EnrollmentEndReason reason)
    {
        var active = await RequireAsync(enrollmentId);
        await _repository.EndEnrollmentAsync(active.Id, reason);
        NotifyChanged();
    }

    public async Task CrossAsync(Guid enrollmentId, int dayIndex, int sessionIndex, string? inkJson = null, Guid? completionId = null)
    {
        var active = await RequireWritableAsync(enrollmentId);
        await _repository.CrossSessionAsync(active.Id, dayIndex, sessionIndex, active.Calendar.DateOf(dayIndex), inkJson, completionId);
        NotifyChanged();
    }

    public async Task UncrossAsync(Guid enrollmentId, int dayIndex, int sessionIndex)
    {
        var active = await RequireWritableAsync(enrollmentId);
        await _repository.UncrossSessionAsync(active.Id, dayIndex, sessionIndex);
        NotifyChanged();
    }

    public async Task ShiftAsync(Guid enrollmentId, DateOnly fromDate, int days)
    {
        var active = await RequireWritableAsync(enrollmentId);
        await _repository.ShiftScheduleAsync(active.Id, fromDate, days, active.Calendar.DayCount);
        NotifyChanged();
    }

    public Task<IReadOnlyList<BonusWorkoutEntity>> GetBonusWorkoutsAsync(DateOnly from, DateOnly to) =>
        _repository.GetBonusWorkoutsAsync(from, to);

    public async Task<BonusWorkoutEntity> LogBonusAsync(string? workoutId, string title, DateOnly date, int? durationMinutes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        var entity = await _repository.LogBonusAsync(workoutId, title.Trim(), date, durationMinutes);
        NotifyChanged();
        return entity;
    }

    public async Task RemoveBonusAsync(Guid id)
    {
        await _repository.RemoveBonusAsync(id);
        NotifyChanged();
    }

    private async Task<ActiveEnrollment> RequireAsync(Guid enrollmentId) =>
        await GetAsync(enrollmentId) ?? throw new InvalidOperationException($"Enrollment {enrollmentId} is not active.");

    private async Task<ActiveEnrollment> RequireWritableAsync(Guid enrollmentId)
    {
        var active = await RequireAsync(enrollmentId);
        return IsReadOnly(active) ? throw new EnrollmentReadOnlyException(enrollmentId) : active;
    }

    // A lapse leaves custom programs visible but read-only, so they can't be started again.
    private void EnsureStartable(WorkoutProgram program)
    {
        if (program.IsCustom && !FeatureGate.CanUse(ProFeature.CustomSchedules, _entitlements.IsPro))
        {
            throw new CustomProgramLockedException();
        }
    }

    private static Guid? ParseId(string? value) => Guid.TryParse(value, out var id) ? id : null;

    // Newest first. A pinned revision that left the catalog falls back to the latest one (warned);
    // an enrollment whose program or track is gone entirely is skipped, not fatal.
    private async Task<IReadOnlyList<ActiveEnrollment>> LoadAsync(IReadOnlyList<EnrollmentEntity> enrollments)
    {
        List<ActiveEnrollment> result = [];
        foreach (var enrollment in enrollments.OrderByDescending(e => e.CreatedAt))
        {
            var program = await _catalog.GetProgramAsync(enrollment.ProgramId, enrollment.ProgramRevision);
            if (program is null && await _catalog.GetProgramAsync(enrollment.ProgramId) is { } latest)
            {
                _logger.LogWarning(
                    "Program '{ProgramId}' revision {Pinned} is not in the catalog; using revision {Latest}.",
                    enrollment.ProgramId,
                    enrollment.ProgramRevision,
                    latest.Revision);
                program = latest;
            }

            if (program?.Tracks.FirstOrDefault(t => t.Id == enrollment.TrackId) is not { } track)
            {
                continue;
            }

            var dayCount = _catalog.GetTrackDays(track).Count;
            if (dayCount == 0)
            {
                continue;
            }

            var shifts = await _repository.GetShiftsAsync(enrollment.Id);
            result.Add(new(enrollment, program, track, new EnrollmentCalendar(enrollment.StartDate, dayCount, shifts)));
        }

        return result;
    }

    private sealed record Snapshot(IReadOnlyList<ActiveEnrollment> All, Guid? PrimaryId)
    {
        /// <summary>The stored choice while it's active, else the most recently started.</summary>
        public ActiveEnrollment? Primary { get; } = All.FirstOrDefault(e => e.Id == PrimaryId) ?? All.FirstOrDefault();
    }

    private void NotifyChanged()
    {
        Interlocked.Increment(ref _version);
        _messenger.Send(new TrackingChangedMessage());
    }
}
