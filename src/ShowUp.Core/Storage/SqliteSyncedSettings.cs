namespace ShowUp.Core.Storage;

public sealed class SqliteSyncedSettings(ShowUpDatabase db, TimeProvider time, IUserScope user, IDeviceIdProvider device)
    : SqliteRepositoryBase(db, time, user, device), ISyncedSettings
{
    private static string KeyOf(string key) => $"k:{key}";

    public async Task<string?> GetAsync(string key)
    {
        string subject = KeyOf(key);
        var connection = await ConnectAsync();
        var rows = await QueryAsync<SyncedSettingEntity>(connection, s => s.SubjectKey == subject);
        return NewestFirst(rows).FirstOrDefault()?.Value;
    }

    public async Task<IReadOnlyDictionary<string, string>> GetByPrefixAsync(string prefix)
    {
        var connection = await ConnectAsync();
        // StartsWith becomes LIKE in SQL, where % and _ in the prefix are wildcards; match the prefix in memory instead.
        var rows = await QueryAsync<SyncedSettingEntity>(connection, s => s.SubjectKey.StartsWith("k:"));
        return rows
            .Where(s => s.Key.StartsWith(prefix, StringComparison.Ordinal))
            .GroupBy(s => s.Key)
            .ToDictionary(g => g.Key, g => NewestFirst(g).First().Value);
    }

    public async Task SetAsync(string key, string value)
    {
        string subject = KeyOf(key);
        var connection = await ConnectAsync();
        await connection.RunInTransactionAsync(c =>
        {
            var existing = NewestFirst(Query<SyncedSettingEntity>(c, s => s.SubjectKey == subject)).ToList();
            if (existing.Count == 0)
            {
                Insert(c, Stamp(new SyncedSettingEntity { SubjectKey = subject, Key = key, Value = value }));
                return;
            }

            if (existing[0].Value != value)
            {
                existing[0].Value = value;
                Touch(c, existing[0]);
            }

            Tombstone(c, existing.Skip(1));
        });
    }
}
