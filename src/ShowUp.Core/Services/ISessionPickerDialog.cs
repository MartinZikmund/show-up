using ShowUp.Core.Catalog;

namespace ShowUp.Core.Services;

/// <summary>An existing workout, or (when <paramref name="Existing"/> is null) a new one of the user's own.</summary>
public sealed record SessionPick(Workout? Existing, string Title, int? DurationMinutes, WorkoutKind Kind, bool Optional);

/// <summary>The schedule builder's "add a workout" dialog: search the catalog (and the user's own workouts) or type a new one.</summary>
public interface ISessionPickerDialog
{
    /// <returns>The pick, or null when cancelled.</returns>
    Task<SessionPick?> ShowAsync(IReadOnlyList<Workout> workouts);
}
