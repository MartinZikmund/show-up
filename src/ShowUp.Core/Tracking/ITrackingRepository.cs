using ShowUp.Core.Storage;

namespace ShowUp.Core.Tracking;

public interface ITrackingRepository
{
    Task<EnrollmentEntity> EnrollAsync(string programId, int revision, string trackId, DateOnly startDate);

    Task<IReadOnlyList<EnrollmentEntity>> GetActiveEnrollmentsAsync();

    Task EndEnrollmentAsync(Guid enrollmentId, EnrollmentEndReason reason);

    Task CrossSessionAsync(Guid enrollmentId, int dayIndex, int sessionIndex, DateOnly dayDate);

    Task UncrossSessionAsync(Guid enrollmentId, int dayIndex, int sessionIndex);

    Task<IReadOnlySet<(int Day, int Session)>> GetCompletedSessionsAsync(Guid enrollmentId);

    Task<IReadOnlyList<CompletedSession>> GetCompletionsAsync(Guid enrollmentId);

    Task<IReadOnlyList<ScheduleShift>> GetShiftsAsync(Guid enrollmentId);

    Task ShiftScheduleAsync(Guid enrollmentId, DateOnly fromDate, int days);

    Task<BonusWorkoutEntity> LogBonusAsync(string? workoutId, string title, DateOnly date, int? durationMinutes);

    Task<IReadOnlyList<BonusWorkoutEntity>> GetBonusWorkoutsAsync(DateOnly from, DateOnly to);

    Task RemoveBonusAsync(Guid id);
}
