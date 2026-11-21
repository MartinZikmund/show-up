using ShowUp.Core.Catalog;
using ShowUp.Core.Storage;

namespace ShowUp.Core.Tracking;

public sealed partial record ActiveEnrollment(EnrollmentEntity Enrollment, WorkoutProgram Program, ProgramTrack Track, EnrollmentCalendar Calendar)
{
    public Guid Id => Enrollment.Id;
}

/// <summary>A write to an enrollment that is read-only because Pro lapsed and it isn't the primary one.</summary>
public sealed class EnrollmentReadOnlyException(Guid enrollmentId)
    : InvalidOperationException($"Enrollment {enrollmentId} is read-only until Pro is back.")
{
    public Guid EnrollmentId { get; } = enrollmentId;
}

/// <summary>
/// The active enrollments, each with its program at the pinned revision. The only path that crosses,
/// uncrosses or shifts, so the day's date always comes from the enrollment's calendar. Free runs one
/// program; Pro runs several, and a lapse leaves all but the primary one read-only.
/// </summary>
public interface IActiveEnrollmentService
{
    /// <summary>Every active enrollment, newest first.</summary>
    Task<IReadOnlyList<ActiveEnrollment>> GetAllAsync();

    /// <summary>The one Calendar and Results show: the stored selection while it's active, else the newest.</summary>
    Task<ActiveEnrollment?> GetSelectedAsync();

    /// <summary>Null when it isn't active (ended, or never was).</summary>
    Task<ActiveEnrollment?> GetAsync(Guid enrollmentId);

    /// <summary>This device's stored selection (may name an ended enrollment).</summary>
    Guid? SelectedEnrollmentId { get; }

    /// <summary>Persists the selection on this device (not synced) and sends a <c>TrackingChangedMessage</c>.</summary>
    Task SelectAsync(Guid enrollmentId);

    /// <summary>The one a lapsed user keeps: the stored choice while it's active, else the most recently started.</summary>
    Task<ActiveEnrollment?> GetPrimaryAsync();

    /// <summary>Whether the stored primary choice names an active enrollment.</summary>
    Task<bool> HasPrimaryChoiceAsync();

    Task SetPrimaryAsync(Guid enrollmentId);

    /// <summary>Not Pro, more programs than Free allows, and not the primary one: no crossing, uncrossing, shifting or journal edits.</summary>
    bool IsReadOnly(ActiveEnrollment enrollment);

    Task<IReadOnlyList<CompletedSession>> GetCompletionsAsync(Guid enrollmentId);

    /// <summary>Bonus workouts logged within the enrollment's calendar.</summary>
    Task<IReadOnlyList<BonusWorkoutEntity>> GetBonusWorkoutsAsync(Guid enrollmentId);

    /// <summary>Starts a program alongside the active ones and selects it. Free with an active program throws: use <see cref="ReplaceAsync"/>.</summary>
    Task<ActiveEnrollment> EnrollAsync(WorkoutProgram program, ProgramTrack track, DateOnly startDate);

    /// <summary>
    /// Ends <paramref name="enrollmentId"/> and starts a new one atomically: a failed start leaves the old one active.
    /// The new one is selected, and becomes primary if the old one was. A read-only enrollment throws <see cref="EnrollmentReadOnlyException"/>.
    /// </summary>
    Task<ActiveEnrollment> ReplaceAsync(Guid enrollmentId, EnrollmentEndReason reason, WorkoutProgram program, ProgramTrack track, DateOnly startDate);

    Task EndAsync(Guid enrollmentId, EnrollmentEndReason reason);

    /// <param name="inkJson">The drawn cross (<c>InkCodec</c> JSON), or null for the marker X.</param>
    /// <param name="completionId">The new completion's id, generated up front so the cross moment can use its seed.</param>
    Task CrossAsync(Guid enrollmentId, int dayIndex, int sessionIndex, string? inkJson = null, Guid? completionId = null);

    Task UncrossAsync(Guid enrollmentId, int dayIndex, int sessionIndex);

    /// <summary>Pushes the schedule back from <paramref name="fromDate"/>; crossed days never move.</summary>
    Task ShiftAsync(Guid enrollmentId, DateOnly fromDate, int days);

    /// <summary>Bonus workouts on any day in the range, with or without an enrollment.</summary>
    Task<IReadOnlyList<BonusWorkoutEntity>> GetBonusWorkoutsAsync(DateOnly from, DateOnly to);

    /// <summary>Logs a workout outside the program; works with no enrollment. Future days and blank titles throw <see cref="ArgumentException"/>.</summary>
    Task<BonusWorkoutEntity> LogBonusAsync(string? workoutId, string title, DateOnly date, int? durationMinutes);

    Task RemoveBonusAsync(Guid id);
}
