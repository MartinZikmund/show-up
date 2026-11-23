using ShowUp.Core.Storage;

namespace ShowUp.Core.Tests.CustomPrograms;

/// <summary>In memory; revisions count up from 1 (the real one uses timestamps).</summary>
internal sealed class FakeCustomProgramRepository : ICustomProgramRepository
{
    private readonly List<CustomProgramEntity> _rows = [];
    private readonly HashSet<string> _deleted = [];

    public int GetAllCalls { get; private set; }

    /// <summary>Like switching to an account that owns nothing.</summary>
    public void Clear()
    {
        _rows.Clear();
        _deleted.Clear();
    }

    /// <summary>When set, <see cref="GetAllAsync"/> signals <see cref="GetAllEntered"/> and waits for it.</summary>
    public TaskCompletionSource? GetAllGate { get; set; }

    public TaskCompletionSource GetAllEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public async Task<IReadOnlyList<CustomProgramEntity>> GetAllAsync()
    {
        GetAllCalls++;
        IReadOnlyList<CustomProgramEntity> rows =
        [
            .. _rows.GroupBy(r => r.ProgramId!).Where(g => !_deleted.Contains(g.Key)).Select(g => g.OrderByDescending(r => r.Revision).First()),
        ];
        if (GetAllGate is { } gate)
        {
            GetAllEntered.TrySetResult();
            await gate.Task;
        }

        return rows;
    }

    public Task<CustomProgramEntity?> GetAsync(string programId, int? revision = null)
    {
        var rows = _rows.Where(r => r.ProgramId == programId);
        return Task.FromResult(revision is { } rev
            ? rows.FirstOrDefault(r => r.Revision == rev)
            : _deleted.Contains(programId) ? null : rows.OrderByDescending(r => r.Revision).FirstOrDefault());
    }

    public Task<IReadOnlyList<int>> GetRevisionsAsync(string programId) =>
        Task.FromResult<IReadOnlyList<int>>([.. _rows.Where(r => r.ProgramId == programId).Select(r => r.Revision).Order()]);

    public Task<(string ProgramId, int Revision)> SaveNewRevisionAsync(string programJson, string? programId = null)
    {
        string id = programId ?? $"custom-{Guid.NewGuid():D}";
        if (_deleted.Contains(id))
        {
            throw new InvalidOperationException("Deleted.");
        }

        int revision = _rows.Where(r => r.ProgramId == id).Select(r => r.Revision).DefaultIfEmpty(0).Max() + 1;
        _rows.Add(new CustomProgramEntity { SubjectKey = $"c:{id}", Revision = revision, ProgramJson = programJson });
        return Task.FromResult((id, revision));
    }

    public Task DeleteAsync(string programId)
    {
        _deleted.Add(programId);
        return Task.CompletedTask;
    }
}
