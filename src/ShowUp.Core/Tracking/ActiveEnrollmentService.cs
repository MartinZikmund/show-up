using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging;
using ShowUp.Core.Catalog;
using ShowUp.Core.Entitlements;
using ShowUp.Core.Messages;
using ShowUp.Core.Storage;

namespace ShowUp.Core.Tracking;

public sealed class ActiveEnrollmentService : IActiveEnrollmentService
{
    public const string SelectedKey = "selected-enrollment";
    public const string PrimaryKey = "primary-enrollment";

    private readonly ITrackingRepository _repository;
    private readonly ICatalogService _catalog;
    private readonly ISyncedSettings _settings;
    private readonly IEntitlementService _entitlements;
    private readonly IMessenger _messenger;
    private readonly ILogger<ActiveEnrollmentService> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IReadOnlyList<ActiveEnrollment> _all = [];
    private Guid? _selectedId;
    private Guid? _primaryId;
    private int _version;
    private int _loadedVersion = -1;

    public ActiveEnrollmentService(
        ITrackingRepository repository,
        ICatalogService catalog,
        ISyncedSettings settings,
        IEntitlementService entitlements,
        IMessenger messenger,
        ILogger<ActiveEnrollmentService> logger)
    {
        _repository = repository;
        _catalog = catalog;
        _settings = settings;
        _entitlements = entitlements;
        _messenger = messenger;
        _logger = logger;

        // Read-only state follows Pro; pages re-read it on the tracking message.
        _entitlements.Changed += (_, _) => _messenger.Send(new TrackingChangedMessage());
    }

    public Guid? SelectedEnrollmentId => _selectedId;

    public async Task<IReadOnlyList<ActiveEnrollment>> GetAllAsync()
    {
        await _gate.WaitAsync();
        try
        {
            // A write during the load bumps the version, so the next call loads again.
            var version = Volatile.Read(ref _version);
            if (_loadedVersion != version)
            {
                _all = await LoadAsync();
                _selectedId = ParseId(await _settings.GetAsync(SelectedKey));
                _primaryId = ParseId(await _settings.GetAsync(PrimaryKey));
                _loadedVersion = version;
            }

            return _all;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<ActiveEnrollment?> GetSelectedAsync()
    {
        var all = await GetAllAsync();
        return all.FirstOrDefault(e => e.Id == _selectedId) ?? all.FirstOrDefault();
    }

    public async Task<ActiveEnrollment?> GetAsync(Guid enrollmentId) =>
        (await GetAllAsync()).FirstOrDefault(e => e.Id == enrollmentId);

    public async Task SelectAsync(Guid enrollmentId)
    {
        if (await GetAsync(enrollmentId) is null)
        {
            throw new InvalidOperationException($"Enrollment {enrollmentId} is not active.");
        }

        await _settings.SetAsync(SelectedKey, enrollmentId.ToString("D"));
        NotifyChanged();
    }

    public async Task<ActiveEnrollment?> GetPrimaryAsync()
    {
        var all = await GetAllAsync();
        return Primary(all);
    }

    public async Task<bool> HasPrimaryChoiceAsync()
    {
        var all = await GetAllAsync();
        return all.Any(e => e.Id == _primaryId);
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
        var all = _all;
        return !_entitlements.IsPro
            && all.Count > FeatureGate.MaxActivePrograms(isPro: false)
            && Primary(all)?.Id != enrollment.Id;
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

        if ((await GetAllAsync()).Count >= FeatureGate.MaxActivePrograms(_entitlements.IsPro))
        {
            throw new InvalidOperationException("Free runs one program at a time; replace the active one instead.");
        }

        var created = await _repository.EnrollAsync(program.Id, program.Revision, track.Id, startDate);
        await _settings.SetAsync(SelectedKey, created.Id.ToString("D"));
        NotifyChanged();
        return await GetAsync(created.Id) ?? throw new InvalidOperationException($"Enrollment in '{program.Id}' could not be resolved.");
    }

    public async Task<ActiveEnrollment> ReplaceAsync(Guid enrollmentId, EnrollmentEndReason reason, WorkoutProgram program, ProgramTrack track, DateOnly startDate)
    {
        ArgumentNullException.ThrowIfNull(program);
        ArgumentNullException.ThrowIfNull(track);

        var old = await RequireAsync(enrollmentId);
        var wasPrimary = _primaryId == old.Id;
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

        await _settings.SetAsync(SelectedKey, created.Id.ToString("D"));
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

    private ActiveEnrollment? Primary(IReadOnlyList<ActiveEnrollment> all) =>
        all.FirstOrDefault(e => e.Id == _primaryId) ?? all.FirstOrDefault();

    private async Task<ActiveEnrollment> RequireAsync(Guid enrollmentId) =>
        await GetAsync(enrollmentId) ?? throw new InvalidOperationException($"Enrollment {enrollmentId} is not active.");

    private async Task<ActiveEnrollment> RequireWritableAsync(Guid enrollmentId)
    {
        var active = await RequireAsync(enrollmentId);
        return IsReadOnly(active) ? throw new EnrollmentReadOnlyException(enrollmentId) : active;
    }

    private static Guid? ParseId(string? value) => Guid.TryParse(value, out var id) ? id : null;

    // Newest first. A pinned revision that left the catalog falls back to the latest one (warned);
    // an enrollment whose program or track is gone entirely is skipped, not fatal.
    private async Task<IReadOnlyList<ActiveEnrollment>> LoadAsync()
    {
        List<ActiveEnrollment> result = [];
        var enrollments = await _repository.GetActiveEnrollmentsAsync();
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

    private void NotifyChanged()
    {
        Interlocked.Increment(ref _version);
        _messenger.Send(new TrackingChangedMessage());
    }
}
