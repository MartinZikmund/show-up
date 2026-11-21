namespace ShowUp.Core.Storage;

public sealed class SqliteCustomProgramRepository(ShowUpDatabase db, TimeProvider time, IUserScope user, IDeviceIdProvider device)
    : SqliteRepositoryBase(db, time, user, device), ICustomProgramRepository
{
    public const string IdPrefix = "custom-";

    private static string KeyOf(string programId) => $"c:{programId}";

    // Two devices can save the same revision; the newest wins.
    private static IEnumerable<CustomProgramEntity> Dedupe(IEnumerable<CustomProgramEntity> rows) => rows
        .GroupBy(p => (p.SubjectKey, p.Revision))
        .Select(g => g.OrderByDescending(p => p.UpdatedAt).First());

    public async Task<IReadOnlyList<CustomProgramEntity>> GetAllAsync()
    {
        var connection = await ConnectAsync();
        var rows = await QueryAsync<CustomProgramEntity>(connection, _ => true);
        return Dedupe(rows)
            .GroupBy(p => p.SubjectKey)
            .Select(g => g.OrderByDescending(p => p.Revision).First())
            .OrderBy(p => p.CreatedAt)
            .ToList();
    }

    public async Task<CustomProgramEntity?> GetAsync(string programId, int? revision = null)
    {
        string key = KeyOf(programId);
        var connection = await ConnectAsync();
        var rows = await QueryAsync<CustomProgramEntity>(connection, p => p.SubjectKey == key);
        var revisions = Dedupe(rows);
        return revision is { } r
            ? revisions.FirstOrDefault(p => p.Revision == r)
            : revisions.OrderByDescending(p => p.Revision).FirstOrDefault();
    }

    public async Task<(string ProgramId, int Revision)> SaveNewRevisionAsync(string programJson, string? programId = null)
    {
        string id = programId ?? $"{IdPrefix}{Guid.NewGuid():D}";
        string key = KeyOf(id);
        var connection = await ConnectAsync();
        int revision = 0;
        await connection.RunInTransactionAsync(c =>
        {
            var existing = Query<CustomProgramEntity>(c, p => p.SubjectKey == key);
            revision = existing.Count == 0 ? 1 : existing.Max(p => p.Revision) + 1;
            c.Insert(Stamp(new CustomProgramEntity { SubjectKey = key, Revision = revision, ProgramJson = programJson }));
        });
        return (id, revision);
    }

    public async Task DeleteAsync(string programId)
    {
        string key = KeyOf(programId);
        var connection = await ConnectAsync();
        await connection.RunInTransactionAsync(c => Tombstone(c, Query<CustomProgramEntity>(c, p => p.SubjectKey == key)));
    }
}
