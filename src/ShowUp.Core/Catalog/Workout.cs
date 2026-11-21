namespace ShowUp.Core.Catalog;

public sealed partial record Workout(string Id, string Name, int? DurationMinutes, WorkoutKind Kind, string? Family);

public sealed record ScheduledSession(string WorkoutId, IReadOnlyList<string>? Alternates = null, bool Optional = false);

public sealed record ScheduleDay(IReadOnlyList<ScheduledSession> Sessions)
{
    public bool IsRestDay(IReadOnlyDictionary<string, Workout> workouts) =>
        Sessions.All(s => workouts.TryGetValue(s.WorkoutId, out var w) && w.Kind == WorkoutKind.Rest);
}

public sealed record ScheduleWeek(IReadOnlyList<ScheduleDay> Days);
