namespace ShowUp.Core.Catalog;

public interface ICatalogService
{
    Task<WorkoutCatalog> GetCatalogAsync();

    /// <summary>Null <paramref name="revision"/> means the latest revision.</summary>
    Task<WorkoutProgram?> GetProgramAsync(string programId, int? revision = null);

    Task<Workout?> GetWorkoutAsync(string workoutId);

    /// <summary>Cached <see cref="ProgramTrack.Days"/>; the record recomputes it on every access.</summary>
    IReadOnlyList<ScheduleDay> GetTrackDays(ProgramTrack track);
}
