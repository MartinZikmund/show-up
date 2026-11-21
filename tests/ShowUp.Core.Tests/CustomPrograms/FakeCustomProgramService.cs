using ShowUp.Core.CustomPrograms;

namespace ShowUp.Core.Tests.CustomPrograms;

internal sealed class FakeCustomProgramService : ICustomProgramService
{
    public bool CanEdit { get; set; } = true;

    public List<CustomProgramDraft> Saved { get; } = [];

    public List<string> Deleted { get; } = [];

    public Exception? SaveFailure { get; set; }

    public int VersionNumber { get; set; } = 1;

    public Task<(string ProgramId, int Revision)> SaveAsync(CustomProgramDraft draft)
    {
        if (!CanEdit)
        {
            throw new CustomProgramLockedException();
        }

        if (SaveFailure is not null)
        {
            return Task.FromException<(string, int)>(SaveFailure);
        }

        Saved.Add(draft);
        return Task.FromResult((draft.ProgramId, Saved.Count));
    }

    public Task DeleteAsync(string programId)
    {
        if (!CanEdit)
        {
            throw new CustomProgramLockedException();
        }

        Deleted.Add(programId);
        return Task.CompletedTask;
    }

    public Task<int> GetVersionNumberAsync(string programId, int revision) => Task.FromResult(VersionNumber);
}
