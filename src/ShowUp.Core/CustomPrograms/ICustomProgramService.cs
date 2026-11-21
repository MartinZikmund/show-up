namespace ShowUp.Core.CustomPrograms;

/// <summary>A save, edit or start of a custom program without Pro (custom programs are read-only after a lapse).</summary>
public sealed class CustomProgramLockedException()
    : InvalidOperationException("Custom schedules are read-only without Pro.");

/// <summary>Saving and deleting the user's own programs; saving needs Pro, deleting never does.</summary>
public interface ICustomProgramService
{
    /// <summary>Pro: build, edit and start custom programs. Without it they stay visible (and deletable) but read-only.</summary>
    bool CanEdit { get; }

    /// <summary>Saves a new revision (enrollments stay pinned to theirs). Throws <see cref="CustomProgramLockedException"/> without Pro.</summary>
    Task<(string ProgramId, int Revision)> SaveAsync(CustomProgramDraft draft);

    /// <summary>Hides the program, with or without Pro; running enrollments keep their pinned revision and history.</summary>
    Task DeleteAsync(string programId);

    /// <summary>1 for the first saved revision, counting up; the stored numbers are timestamps.</summary>
    Task<int> GetVersionNumberAsync(string programId, int revision);
}
