namespace ShowUp.Core.Storage;

/// <summary>
/// One logged value of a workout metric: for a scheduled session (enrollment, day, session)
/// or for a bonus workout (<see cref="BonusWorkoutId"/>, the enrollment fields left null).
/// </summary>
public sealed class MetricEntryEntity : SyncableEntity
{
    public Guid? EnrollmentId { get; set; }

    public int? DayIndex { get; set; }

    public int? SessionIndex { get; set; }

    public Guid? BonusWorkoutId { get; set; }

    public string WorkoutId { get; set; } = "";

    public string MetricId { get; set; } = "";

    public double? Value { get; set; }

    public string? TextValue { get; set; }
}
