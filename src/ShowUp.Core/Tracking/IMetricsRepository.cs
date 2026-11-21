using ShowUp.Core.Storage;

namespace ShowUp.Core.Tracking;

/// <summary>What a logged value belongs to: a scheduled session of an enrollment, or a bonus workout.</summary>
public sealed record MetricTarget(string WorkoutId, Guid? EnrollmentId = null, int? DayIndex = null, int? SessionIndex = null, Guid? BonusWorkoutId = null)
{
    public static MetricTarget ForSession(Guid enrollmentId, int dayIndex, int sessionIndex, string workoutId) =>
        new(workoutId, enrollmentId, dayIndex, sessionIndex);

    public static MetricTarget ForBonus(Guid bonusWorkoutId, string workoutId) => new(workoutId, BonusWorkoutId: bonusWorkoutId);

    public bool IsBonus => BonusWorkoutId is not null;

    public bool Matches(MetricEntryEntity entry) =>
        IsBonus
            ? entry.BonusWorkoutId == BonusWorkoutId
            : entry.BonusWorkoutId is null && entry.EnrollmentId == EnrollmentId && entry.DayIndex == DayIndex && entry.SessionIndex == SessionIndex;
}

/// <summary>A metric's value: a number (reps, seconds, kg...) or, for text metrics, a string.</summary>
public readonly record struct MetricValue(double? Number, string? Text = null)
{
    public bool IsEmpty => Number is null && string.IsNullOrWhiteSpace(Text);

    public static MetricValue Of(MetricEntryEntity entry) => new(entry.Value, entry.TextValue);
}

public interface IMetricsRepository
{
    /// <summary>Live entries logged for the enrollment's scheduled sessions.</summary>
    Task<IReadOnlyList<MetricEntryEntity>> GetSessionEntriesAsync(Guid enrollmentId);

    Task<IReadOnlyList<MetricEntryEntity>> GetBonusEntriesAsync(IReadOnlyCollection<Guid> bonusWorkoutIds);

    /// <summary>Upserts by (target, metric); an empty value deletes the entry.</summary>
    Task SetValueAsync(MetricTarget target, string metricId, MetricValue value);
}
