using ShowUp.Core.Catalog;
using ShowUp.Core.Storage;

namespace ShowUp.Core.Tracking;

public sealed record ActiveEnrollment(EnrollmentEntity Enrollment, WorkoutProgram Program, ProgramTrack Track, EnrollmentCalendar Calendar);

/// <summary>
/// The enrollment the app tracks, with its program at the pinned revision. The only path that crosses,
/// uncrosses or shifts, so the day's date always comes from the enrollment's calendar.
/// </summary>
public interface IActiveEnrollmentService
{
    /// <summary>Raised after every write; <see cref="Messages.TrackingChangedMessage"/> goes out at the same time.</summary>
    event EventHandler? Changed;

    Task<ActiveEnrollment?> GetAsync();

    Task<IReadOnlyList<CompletedSession>> GetCompletionsAsync();

    /// <summary>Bonus workouts logged within the enrollment's calendar.</summary>
    Task<IReadOnlyList<BonusWorkoutEntity>> GetBonusWorkoutsAsync();

    Task<ActiveEnrollment> EnrollAsync(WorkoutProgram program, ProgramTrack track, DateOnly startDate);

    Task EndAsync(EnrollmentEndReason reason);

    /// <param name="inkJson">The drawn cross (<c>InkCodec</c> JSON), or null for the marker X.</param>
    Task CrossAsync(int dayIndex, int sessionIndex, string? inkJson = null);

    Task UncrossAsync(int dayIndex, int sessionIndex);

    /// <summary>Pushes the schedule back from <paramref name="fromDate"/>; crossed days never move.</summary>
    Task ShiftAsync(DateOnly fromDate, int days);

    Task LogBonusAsync(string title, DateOnly date);
}
