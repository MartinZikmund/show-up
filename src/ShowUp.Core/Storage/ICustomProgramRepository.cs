namespace ShowUp.Core.Storage;

public interface ICustomProgramRepository
{
    /// <summary>The latest live revision of each program.</summary>
    Task<IReadOnlyList<CustomProgramEntity>> GetAllAsync();

    /// <summary>A specific revision, or the latest when <paramref name="revision"/> is null.</summary>
    Task<CustomProgramEntity?> GetAsync(string programId, int? revision = null);

    /// <summary>Saves a new revision; a null <paramref name="programId"/> creates the program (<c>custom-{guid}</c>).</summary>
    Task<(string ProgramId, int Revision)> SaveNewRevisionAsync(string programJson, string? programId = null);

    /// <summary>Tombstones every revision.</summary>
    Task DeleteAsync(string programId);
}
