using AwesomeAssertions;
using ShowUp.Core.Storage;

namespace ShowUp.Core.Tests.Storage;

[TestClass]
public sealed class SyncStampTests : StorageTestBase
{
    [TestMethod]
    public async Task Insert_OddTicks_StampsWholeMicroseconds()
    {
        Time.Advance(TimeSpan.FromTicks(1234567));

        var reminder = await CreateReminders().AddAsync(60, 1);

        reminder.CreatedAt.UtcTicks.Should().Be(reminder.CreatedAt.UtcTicks / 10 * 10);
        reminder.UpdatedAt.Should().Be(Timestamps.Truncate(Time.GetUtcNow()));
        (await StoredAsync<ReminderEntity>(reminder.Id)).UpdatedAt.Should().Be(reminder.UpdatedAt);
    }

    [TestMethod]
    public async Task Touch_PreviousVersionFromAheadClock_StampsAfterIt()
    {
        var repo = CreateReminders();
        var reminder = await repo.AddAsync(60, 1);
        var ahead = Time.GetUtcNow() + TimeSpan.FromMinutes(10);
        var connection = await Db.GetConnectionAsync();
        await connection.ExecuteAsync("""UPDATE "ReminderEntity" SET "UpdatedAt" = ?""", ahead.UtcTicks);

        await repo.SetEnabledAsync(reminder.Id, false);

        (await StoredAsync<ReminderEntity>(reminder.Id)).UpdatedAt.Should().Be(ahead + Timestamps.Microsecond);
    }

    [TestMethod]
    public async Task Touch_SameMicrosecond_StillAdvances()
    {
        var repo = CreateReminders();
        var reminder = await repo.AddAsync(60, 1);

        await repo.SetEnabledAsync(reminder.Id, false);
        await repo.SetEnabledAsync(reminder.Id, true);

        (await StoredAsync<ReminderEntity>(reminder.Id)).UpdatedAt.Should().Be(reminder.UpdatedAt + 2 * Timestamps.Microsecond);
    }

    [TestMethod]
    public async Task Tombstone_StampsDeletedAtWithTheNewVersion()
    {
        var repo = CreateReminders();
        var reminder = await repo.AddAsync(60, 1);

        await repo.DeleteAsync(reminder.Id);

        var stored = await StoredAsync<ReminderEntity>(reminder.Id);
        stored.DeletedAt.Should().Be(stored.UpdatedAt);
        stored.UpdatedAt.Should().BeAfter(reminder.UpdatedAt);
    }

    [TestMethod]
    public async Task Writes_MarkRowPendingAndRaiseLocalChanged()
    {
        int changes = 0;
        Db.LocalChanged += (_, _) => changes++;
        var repo = CreateReminders();
        var reminder = await repo.AddAsync(60, 1);
        var connection = await Db.GetConnectionAsync();
        await connection.ExecuteAsync("""UPDATE "ReminderEntity" SET "SyncPending" = 0""");

        await repo.SetEnabledAsync(reminder.Id, false);

        (await StoredAsync<ReminderEntity>(reminder.Id)).SyncPending.Should().BeTrue();
        changes.Should().Be(2);
    }

    [TestMethod]
    public async Task Claim_MarksClaimedRowsPending()
    {
        var reminder = await CreateReminders().AddAsync(60, 1);
        var connection = await Db.GetConnectionAsync();
        await connection.ExecuteAsync("""UPDATE "ReminderEntity" SET "SyncPending" = 0""");

        await new SqliteLocalDataClaim(Db, Time, Device).ClaimAsync("alice");

        var stored = await StoredAsync<ReminderEntity>(reminder.Id);
        stored.UserId.Should().Be("alice");
        stored.SyncPending.Should().BeTrue();
    }

    private async Task<T> StoredAsync<T>(Guid id)
        where T : SyncableEntity, new()
    {
        var connection = await Db.GetConnectionAsync();
        return await connection.FindAsync<T>(id);
    }
}
