namespace ShowUp.Core.Storage;

public sealed class SqliteLocalDataClaim(ShowUpDatabase db, TimeProvider time, IDeviceIdProvider device) : ILocalDataClaim
{
    private const string Settings = nameof(SyncedSettingEntity);

    /// <summary>Every synced table (docs/sync-classification.md); a test ties it to the <see cref="SyncableEntity"/> types.</summary>
    public static IReadOnlyList<string> SyncedTables { get; } =
    [
        nameof(EnrollmentEntity),
        nameof(CompletionEntity),
        nameof(ScheduleShiftEntity),
        nameof(BonusWorkoutEntity),
        nameof(NoteEntity),
        nameof(MetricEntryEntity),
        nameof(ReminderEntity),
        nameof(CustomProgramEntity),
        Settings,
    ];

    private const string Live = """ WHERE "UserId" IS NULL AND "DeletedAt" IS NULL""";

    public async Task<AnonymousProgress> GetAnonymousProgressAsync()
    {
        var connection = await db.GetConnectionAsync();
        var programs = await connection.ExecuteScalarAsync<int>($"""SELECT COUNT(*) FROM "{nameof(EnrollmentEntity)}"{Live}""");
        var crossed = await connection.ExecuteScalarAsync<int>($"""SELECT COUNT(*) FROM "{nameof(CompletionEntity)}"{Live}""");
        // One custom program has a row per revision.
        var other = await connection.ExecuteScalarAsync<int>($"""SELECT COUNT(DISTINCT "SubjectKey") FROM "{nameof(CustomProgramEntity)}"{Live}""");
        foreach (var table in SyncedTables.Except([nameof(EnrollmentEntity), nameof(CompletionEntity), nameof(CustomProgramEntity), Settings]))
        {
            other += await connection.ExecuteScalarAsync<int>($"""SELECT COUNT(*) FROM "{table}"{Live}""");
        }

        return new(programs, crossed, other);
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
            foreach (var table in SyncedTables)
            {
                // Settings keep their own time: the account's settings from other devices are newer and must win.
                var stamp = table == Settings ? "" : """, "UpdatedAt" = MAX(?, ("UpdatedAt" / 10 + 1) * 10)""";
                object[] args = table == Settings ? [userId, device.DeviceId] : [userId, device.DeviceId, now];
                claimed += c.Execute($"""UPDATE "{table}" SET "UserId" = ?, "DeviceId" = ?, "SyncPending" = 1{stamp}{Live}""", args);
            }
        });
        if (claimed > 0)
        {
            db.NotifyLocalChange();
        }

        return claimed;
    }
}
