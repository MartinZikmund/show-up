namespace ShowUp.Core.Catalog;

public interface ICatalogService
{
    /// <summary>The bundled catalog plus the user's custom programs (latest revisions) and their workouts.</summary>
    Task<WorkoutCatalog> GetCatalogAsync();

    /// <summary>Re-reads the bundled catalog and the custom programs on the next call, after the stored data changed behind the service's back.</summary>
    void Invalidate();

    /// <summary>Re-reads custom programs on the next call, after one is saved or deleted.</summary>
    void InvalidateCustomPrograms();

    /// <summary>Null <paramref name="revision"/> means the latest revision; a pinned custom revision resolves even after edits or deletion.</summary>
    Task<WorkoutProgram?> GetProgramAsync(string programId, int? revision = null);

    Task<Workout?> GetWorkoutAsync(string workoutId);

    /// <summary>Cached <see cref="ProgramTrack.Days"/>; the record recomputes it on every access.</summary>
    IReadOnlyList<ScheduleDay> GetTrackDays(ProgramTrack track);
}
