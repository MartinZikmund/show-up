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
        return rows.OrderByDescending(s => s.UpdatedAt).FirstOrDefault()?.Value;
    }

    public async Task SetAsync(string key, string value)
    {
        string subject = KeyOf(key);
        var connection = await ConnectAsync();
        await connection.RunInTransactionAsync(c =>
        {
            var existing = Query<SyncedSettingEntity>(c, s => s.SubjectKey == subject).OrderByDescending(s => s.UpdatedAt).ToList();
            if (existing.Count == 0)
            {
                c.Insert(Stamp(new SyncedSettingEntity { SubjectKey = subject, Key = key, Value = value }));
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
