using ShowUp.Core.Storage;

namespace ShowUp.Core.Tracking;

public interface ITrackingRepository
{
    Task<EnrollmentEntity> EnrollAsync(string programId, int revision, string trackId, DateOnly startDate);

    Task<IReadOnlyList<EnrollmentEntity>> GetActiveEnrollmentsAsync();

    Task EndEnrollmentAsync(Guid enrollmentId, EnrollmentEndReason reason);

    /// <param name="inkJson">The user's drawn strokes (<c>InkCodec</c> JSON), or null for the marker X.</param>
    Task CrossSessionAsync(Guid enrollmentId, int dayIndex, int sessionIndex, DateOnly dayDate, string? inkJson = null);

    Task UncrossSessionAsync(Guid enrollmentId, int dayIndex, int sessionIndex);

    Task<IReadOnlySet<(int Day, int Session)>> GetCompletedSessionsAsync(Guid enrollmentId);

    Task<IReadOnlyList<CompletedSession>> GetCompletionsAsync(Guid enrollmentId);

    Task<IReadOnlyList<ScheduleShift>> GetShiftsAsync(Guid enrollmentId);

    /// <summary>Shifts the schedule by 1 to 30 days; a day that is already crossed never moves.</summary>
    Task ShiftScheduleAsync(Guid enrollmentId, DateOnly fromDate, int days, int dayCount);

    Task<BonusWorkoutEntity> LogBonusAsync(string? workoutId, string title, DateOnly date, int? durationMinutes);

    Task<IReadOnlyList<BonusWorkoutEntity>> GetBonusWorkoutsAsync(DateOnly from, DateOnly to);

    Task RemoveBonusAsync(Guid id);
}
