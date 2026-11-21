using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging;
using ShowUp.Core.Catalog;
using ShowUp.Core.Messages;
using ShowUp.Core.Storage;

namespace ShowUp.Core.Tracking;

public sealed class ActiveEnrollmentService(
    ITrackingRepository repository,
    ICatalogService catalog,
    IMessenger messenger,
    ILogger<ActiveEnrollmentService> logger) : IActiveEnrollmentService
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private ActiveEnrollment? _active;
    private int _version;
    private int _loadedVersion = -1;

    public async Task<ActiveEnrollment?> GetAsync()
    {
        await _gate.WaitAsync();
        try
        {
            // A write during the load bumps the version, so the next call loads again.
            var version = Volatile.Read(ref _version);
            if (_loadedVersion != version)
            {
                _active = await LoadAsync();
                _loadedVersion = version;
            }

            return _active;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<CompletedSession>> GetCompletionsAsync() =>
        await GetAsync() is { } active ? await repository.GetCompletionsAsync(active.Enrollment.Id) : [];

    public async Task<IReadOnlyList<BonusWorkoutEntity>> GetBonusWorkoutsAsync() =>
        await GetAsync() is { } active
            ? await repository.GetBonusWorkoutsAsync(active.Calendar.DateOf(0), active.Calendar.EndDate)
            : [];

    public async Task<ActiveEnrollment> EnrollAsync(WorkoutProgram program, ProgramTrack track, DateOnly startDate)
    {
        ArgumentNullException.ThrowIfNull(program);
        ArgumentNullException.ThrowIfNull(track);

        await repository.EnrollAsync(program.Id, program.Revision, track.Id, startDate);
        NotifyChanged();
        return await GetAsync() ?? throw new InvalidOperationException($"Enrollment in '{program.Id}' could not be resolved.");
    }

    public async Task<ActiveEnrollment> ReplaceAsync(EnrollmentEndReason reason, WorkoutProgram program, ProgramTrack track, DateOnly startDate)
    {
        ArgumentNullException.ThrowIfNull(program);
        ArgumentNullException.ThrowIfNull(track);

        if (await GetAsync() is not { } active)
        {
            return await EnrollAsync(program, track, startDate);
        }

        try
        {
            await repository.ReplaceEnrollmentAsync(active.Enrollment.Id, reason, program.Id, program.Revision, track.Id, startDate);
        }
        finally
        {
            NotifyChanged();
        }

        return await GetAsync() ?? throw new InvalidOperationException($"Enrollment in '{program.Id}' could not be resolved.");
    }

    public async Task EndAsync(EnrollmentEndReason reason)
    {
        var active = await RequireAsync();
        await repository.EndEnrollmentAsync(active.Enrollment.Id, reason);
        NotifyChanged();
    }

    public async Task CrossAsync(int dayIndex, int sessionIndex, string? inkJson = null, Guid? completionId = null)
    {
        var active = await RequireAsync();
        await repository.CrossSessionAsync(active.Enrollment.Id, dayIndex, sessionIndex, active.Calendar.DateOf(dayIndex), inkJson, completionId);
        NotifyChanged();
    }

    public async Task UncrossAsync(int dayIndex, int sessionIndex)
    {
        var active = await RequireAsync();
        await repository.UncrossSessionAsync(active.Enrollment.Id, dayIndex, sessionIndex);
        NotifyChanged();
    }

    public async Task ShiftAsync(DateOnly fromDate, int days)
    {
        var active = await RequireAsync();
        await repository.ShiftScheduleAsync(active.Enrollment.Id, fromDate, days, active.Calendar.DayCount);
        NotifyChanged();
    }

    public Task<IReadOnlyList<BonusWorkoutEntity>> GetBonusWorkoutsAsync(DateOnly from, DateOnly to) =>
        repository.GetBonusWorkoutsAsync(from, to);

    public async Task<BonusWorkoutEntity> LogBonusAsync(string? workoutId, string title, DateOnly date, int? durationMinutes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        var entity = await repository.LogBonusAsync(workoutId, title.Trim(), date, durationMinutes);
        NotifyChanged();
        return entity;
    }

    public async Task RemoveBonusAsync(Guid id)
    {
        await repository.RemoveBonusAsync(id);
        NotifyChanged();
    }

    private async Task<ActiveEnrollment> RequireAsync() =>
        await GetAsync() ?? throw new InvalidOperationException("There is no active enrollment.");

    // Newest first. A pinned revision that left the catalog falls back to the latest one (warned);
    // an enrollment whose program or track is gone entirely is skipped, not fatal.
    private async Task<ActiveEnrollment?> LoadAsync()
    {
        var enrollments = await repository.GetActiveEnrollmentsAsync();
        foreach (var enrollment in enrollments.OrderByDescending(e => e.CreatedAt))
        {
            var program = await catalog.GetProgramAsync(enrollment.ProgramId, enrollment.ProgramRevision);
            if (program is null && await catalog.GetProgramAsync(enrollment.ProgramId) is { } latest)
            {
                logger.LogWarning(
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

            var dayCount = catalog.GetTrackDays(track).Count;
            if (dayCount == 0)
            {
                continue;
            }

            var shifts = await repository.GetShiftsAsync(enrollment.Id);
            return new(enrollment, program, track, new EnrollmentCalendar(enrollment.StartDate, dayCount, shifts));
        }

        return null;
    }

    private void NotifyChanged()
    {
        Interlocked.Increment(ref _version);
        messenger.Send(new TrackingChangedMessage());
    }
}
