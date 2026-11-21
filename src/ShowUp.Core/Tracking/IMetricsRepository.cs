using System.Globalization;
using ShowUp.Core.Storage;

namespace ShowUp.Core.Tracking;

/// <summary>What a logged value belongs to: exactly one of a scheduled session of an enrollment, or a bonus workout.</summary>
public sealed record MetricTarget
{
    private MetricTarget(string workoutId, Guid? enrollmentId, int? dayIndex, int? sessionIndex, Guid? bonusWorkoutId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workoutId);
        WorkoutId = workoutId;
        EnrollmentId = enrollmentId;
        DayIndex = dayIndex;
        SessionIndex = sessionIndex;
        BonusWorkoutId = bonusWorkoutId;
    }

    public string WorkoutId { get; }

    public Guid? EnrollmentId { get; }

    public int? DayIndex { get; }

    public int? SessionIndex { get; }

    public Guid? BonusWorkoutId { get; }

    public bool IsBonus => BonusWorkoutId is not null;

    /// <summary>The natural key the row is upserted by (and a future server unique constraint).</summary>
    public string SubjectKey => BonusWorkoutId is { } bonus
        ? SubjectKeys.Bonus(bonus)
        : SubjectKeys.Session(EnrollmentId!.Value, DayIndex!.Value, SessionIndex!.Value);

    public static MetricTarget ForSession(Guid enrollmentId, int dayIndex, int sessionIndex, string workoutId)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(dayIndex);
        ArgumentOutOfRangeException.ThrowIfNegative(sessionIndex);
        return new(workoutId, enrollmentId, dayIndex, sessionIndex, null);
    }

    public static MetricTarget ForBonus(Guid bonusWorkoutId, string workoutId) => new(workoutId, null, null, null, bonusWorkoutId);

    public bool Matches(MetricEntryEntity entry) => entry.SubjectKey == SubjectKey && entry.WorkoutId == WorkoutId;
}

/// <summary>Natural keys of notes and results: <c>p:</c> program, <c>d:</c> day, <c>s:</c> session, <c>b:</c> bonus workout.</summary>
public static class SubjectKeys
{
    public static string Program(Guid enrollmentId) => $"p:{enrollmentId:D}";

    public static string Day(Guid enrollmentId, int dayIndex) => string.Create(CultureInfo.InvariantCulture, $"d:{enrollmentId:D}:{dayIndex}");

    public static string Session(Guid enrollmentId, int dayIndex, int sessionIndex) =>
        string.Create(CultureInfo.InvariantCulture, $"s:{enrollmentId:D}:{dayIndex}:{sessionIndex}");

    public static string Bonus(Guid bonusWorkoutId) => $"b:{bonusWorkoutId:D}";

    public static string Note(Guid enrollmentId, int? dayIndex, int? sessionIndex) => (dayIndex, sessionIndex) switch
    {
        (null, null) => Program(enrollmentId),
        ({ } day, null) => Day(enrollmentId, day),
        ({ } day, { } session) => Session(enrollmentId, day, session),
        _ => throw new ArgumentException("A session note needs its day.", nameof(sessionIndex)),
    };
}

/// <summary>A metric's value: a number in SI units (reps, seconds, kg, km) or, for text metrics, a string.</summary>
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

    /// <summary>Upserts every value by (subject, metric) in one transaction; an empty value deletes the entry.</summary>
    Task SetValuesAsync(MetricTarget target, IReadOnlyDictionary<string, MetricValue> values);
}
