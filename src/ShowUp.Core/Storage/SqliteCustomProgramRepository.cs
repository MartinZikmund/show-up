using System.Globalization;
using Microsoft.Extensions.Logging;
using ShowUp.Core.CustomPrograms;

namespace ShowUp.Core.Storage;

public sealed class SqliteCustomProgramRepository(
    ShowUpDatabase db,
    TimeProvider time,
    IUserScope user,
    IDeviceIdProvider device,
    ISyncedSettings settings,
    ILogger<SqliteCustomProgramRepository> logger)
    : SqliteRepositoryBase(db, time, user, device), ICustomProgramRepository
{
    public const string IdPrefix = CustomProgramFactory.IdPrefix;

    private const string DeletedPrefix = "custom-deleted:";

    // Revisions are seconds since this, so two offline devices practically never pick the same number.
    private static readonly DateTimeOffset _revisionEpoch = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static string KeyOf(string programId) => $"c:{programId}";

    // Two devices can save the same revision; the newest wins.
    private static IEnumerable<CustomProgramEntity> Dedupe(IEnumerable<CustomProgramEntity> rows) => rows
        .GroupBy(p => (p.SubjectKey, p.Revision))
        .Select(g => NewestFirst(g).First());

    public async Task<IReadOnlyList<CustomProgramEntity>> GetAllAsync()
    {
        var connection = await ConnectAsync();
        var rows = await QueryAsync<CustomProgramEntity>(connection, _ => true);
        var markers = await settings.GetByPrefixAsync(DeletedPrefix);
        List<(DateTimeOffset Created, CustomProgramEntity Program)> current = [];
        foreach (var group in rows.GroupBy(p => p.SubjectKey))
        {
            if (!group.Key.StartsWith("c:", StringComparison.Ordinal))
            {
                continue;
            }

            string programId = group.Key[2..];
            markers.TryGetValue(DeletedPrefix + programId, out string? marker);
            if (!IsDeleted(programId, marker, group))
            {
                current.Add((group.Min(p => p.CreatedAt), Dedupe(group).OrderByDescending(p => p.Revision).First()));
            }
        }

        return current.OrderBy(c => c.Created).ThenBy(c => c.Program.SubjectKey, StringComparer.Ordinal).Select(c => c.Program).ToList();
    }

    public async Task<CustomProgramEntity?> GetAsync(string programId, int? revision = null)
    {
        string key = KeyOf(programId);
        var connection = await ConnectAsync();
        var rows = await QueryAsync<CustomProgramEntity>(connection, p => p.SubjectKey == key);
        var revisions = Dedupe(rows);
        if (revision is { } r)
        {
            return revisions.FirstOrDefault(p => p.Revision == r);
        }

        return await IsDeletedAsync(programId, rows) ? null : revisions.OrderByDescending(p => p.Revision).FirstOrDefault();
    }

    public async Task<IReadOnlyList<int>> GetRevisionsAsync(string programId)
    {
        string key = KeyOf(programId);
        var connection = await ConnectAsync();
        var rows = await QueryAsync<CustomProgramEntity>(connection, p => p.SubjectKey == key);
        return [.. rows.Select(p => p.Revision).Distinct().Order()];
    }

    public async Task<(string ProgramId, int Revision)> SaveNewRevisionAsync(string programJson, string? programId = null)
    {
        string id = programId ?? $"{IdPrefix}{Guid.NewGuid():D}";
        string key = KeyOf(id);
        var connection = await ConnectAsync();
        if (programId is not null)
        {
            var rows = await QueryAsync<CustomProgramEntity>(connection, p => p.SubjectKey == key);
            if (await IsDeletedAsync(id, rows))
            {
                throw new InvalidOperationException($"Program '{id}' was deleted; a new program needs a new id.");
            }
        }

        int stamp = (int)(UtcNow - _revisionEpoch).TotalSeconds;
        int revision = 0;
        await connection.RunInTransactionAsync(c =>
        {
            var existing = Query<CustomProgramEntity>(c, p => p.SubjectKey == key);
            revision = Math.Max(existing.Count == 0 ? 1 : existing.Max(p => p.Revision) + 1, stamp);
            c.Insert(Stamp(new CustomProgramEntity { SubjectKey = key, Revision = revision, ProgramJson = programJson }));
        });
        return (id, revision);
    }

    public async Task DeleteAsync(string programId)
    {
        string key = KeyOf(programId);
        var connection = await ConnectAsync();
        var rows = await QueryAsync<CustomProgramEntity>(connection, p => p.SubjectKey == key);
        if (rows.Count > 0)
        {
            await settings.SetAsync(DeletedPrefix + programId, UtcNow.ToString("O", CultureInfo.InvariantCulture));
        }
    }

    private async Task<bool> IsDeletedAsync(string programId, IReadOnlyList<CustomProgramEntity> rows) =>
        IsDeleted(programId, await settings.GetAsync(DeletedPrefix + programId), rows);

    // A marker hides every revision saved at or before it; one that can't be read hides the program (safer than resurrecting it).
    private bool IsDeleted(string programId, string? marker, IEnumerable<CustomProgramEntity> rows)
    {
        if (marker is null)
        {
            return false;
        }

        if (!DateTimeOffset.TryParse(marker, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var deletedAt))
        {
            logger.LogWarning("Unreadable delete marker for custom program {ProgramId}; treating it as deleted", programId);
            return true;
        }

        return !rows.Any() || rows.Max(p => p.UpdatedAt) <= deletedAt;
    }
}
