using ShowUp.Core.Catalog;

namespace ShowUp.Core.Stats;

/// <summary>One workout done: a crossed session that isn't rest, or a bonus workout.</summary>
/// <param name="Date">The local calendar day it counts for.</param>
/// <param name="Minutes">0 when the workout has no known length, which is common: the catalog verifies calendars, not durations.</param>
public sealed record ActivityEntry(DateOnly Date, int Minutes, WorkoutKind Kind, bool IsBonus);

public sealed record WeekBucket(DateOnly WeekStart, int Workouts, int Minutes);

public sealed record KindBucket(WorkoutKind Kind, int Workouts, int Minutes);

public sealed record HeatDay(DateOnly Date, int Workouts);

/// <param name="ScheduledDays">Training days (not rest) up to today, or up to the day the program ended.</param>
/// <param name="CompletionRate">0 to 1; 0 when nothing was scheduled yet.</param>
public sealed record ProgramComparison(Guid EnrollmentId, string Name, int CrossedDays, int ScheduledDays, int LongestStreak, double CompletionRate);

/// <param name="Weeks">Oldest first, ending with the current week; empty weeks included.</param>
/// <param name="ByKind">Most workouts first (many catalog workouts have no known length); kinds with nothing are left out.</param>
/// <param name="Heatmap">Whole weeks, oldest day first, ending on the last day of the current week.</param>
public sealed record StatsSummary(
    int TotalWorkouts,
    int TotalMinutes,
    int ActiveDays,
    IReadOnlyList<WeekBucket> Weeks,
    IReadOnlyList<KindBucket> ByKind,
    IReadOnlyList<HeatDay> Heatmap,
    IReadOnlyList<ProgramComparison> Programs);
