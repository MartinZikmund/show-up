namespace ShowUp.Core.Storage;

public sealed class SqliteLocalDataClaim(ShowUpDatabase db, TimeProvider time, IDeviceIdProvider device) : ILocalDataClaim
{
    // Every synced table (docs/sync-classification.md); settings alone don't count as progress.
    private static readonly string[] _progressTables =
    [
        "EnrollmentEntity",
        "CompletionEntity",
        "ScheduleShiftEntity",
        "BonusWorkoutEntity",
        "NoteEntity",
        "MetricEntryEntity",
        "ReminderEntity",
        "CustomProgramEntity",
    ];

    private static readonly string[] _syncedTables = [.. _progressTables, "SyncedSettingEntity"];

    public async Task<bool> HasAnonymousProgressAsync()
    {
        var connection = await db.GetConnectionAsync();
        foreach (var table in _progressTables)
        {
            if (await connection.ExecuteScalarAsync<int>($"""SELECT EXISTS (SELECT 1 FROM "{table}" WHERE "UserId" IS NULL AND "DeletedAt" IS NULL)""") == 1)
            {
                return true;
            }
        }

        return false;
    }

    public async Task<int> ClaimAsync(string userId)
    {
        ArgumentException.ThrowIfNullOrEmpty(userId);

        // UTC ticks, as sqlite-net stores DateTimeOffset; whole microseconds so the server keeps the exact value,
        // and always later than the row's own stamp so last-writer-wins on other devices picks the claimed copy.
        long now = time.GetUtcNow().UtcTicks / 10 * 10;
        var connection = await db.GetConnectionAsync();
        int claimed = 0;
        await connection.RunInTransactionAsync(c =>
        {
            foreach (var table in _syncedTables)
            {
                claimed += c.Execute(
                    $"""UPDATE "{table}" SET "UserId" = ?, "DeviceId" = ?, "UpdatedAt" = MAX(?, ("UpdatedAt" / 10 + 1) * 10) WHERE "UserId" IS NULL AND "DeletedAt" IS NULL""",
                    userId,
                    device.DeviceId,
                    now);
            }
        });
        return claimed;
    }
}
