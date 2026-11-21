namespace ShowUp.Core.Services;

public sealed record BonusWorkoutEntry(string? WorkoutId, string Title, int? DurationMinutes, DateOnly Date);

/// <summary>The "log a bonus workout" dialog: a catalog workout or a custom title, a duration and a day.</summary>
public interface IBonusWorkoutDialog
{
    /// <param name="date">The day it starts on; the user can pick another, but never a future one.</param>
    /// <returns>What to log, or null when cancelled.</returns>
    Task<BonusWorkoutEntry?> ShowAsync(DateOnly date);
}
