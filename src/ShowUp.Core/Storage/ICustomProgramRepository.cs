namespace ShowUp.Core.Storage;

public interface ICustomProgramRepository
{
    /// <summary>The latest live revision of each program.</summary>
    Task<IReadOnlyList<CustomProgramEntity>> GetAllAsync();

    /// <summary>
    /// The latest revision (null when the program is deleted), or, with an explicit <paramref name="revision"/>,
    /// exactly that one even if the program is deleted, so enrollments pinned to it keep resolving.
    /// </summary>
    Task<CustomProgramEntity?> GetAsync(string programId, int? revision = null);

    /// <summary>Every saved revision number of the program, oldest first (deleted programs included).</summary>
    Task<IReadOnlyList<int>> GetRevisionsAsync(string programId);

    /// <summary>
    /// Saves a new revision (number = max(latest + 1, seconds since 2026-01-01), so offline devices don't collide);
    /// a null <paramref name="programId"/> creates the program (<c>custom-{guid}</c>). Throws for a deleted id.
    /// </summary>
    Task<(string ProgramId, int Revision)> SaveNewRevisionAsync(string programJson, string? programId = null);

    /// <summary>Hides the program: writes a synced <c>custom-deleted:{id}</c> marker (revisions are kept for pinned enrollments). Unknown ids are ignored.</summary>
    Task DeleteAsync(string programId);
}
