using AwesomeAssertions;
using ShowUp.Core.Storage;
using ShowUp.Core.Tests.Storage;
using ShowUp.Core.Tracking;

namespace ShowUp.Core.Tests.Accounts;

[TestClass]
public sealed class LocalDataClaimTests : StorageTestBase
{
    [TestMethod]
    public async Task Claim_EverySyncedTable_MovesOnlyLiveSignedOutRows()
    {
        var tracking = CreateRepository();
        var enrollment = await tracking.EnrollAsync("p", 1, "t", new DateOnly(2026, 12, 1));
        await tracking.ShiftScheduleAsync(enrollment.Id, new DateOnly(2026, 12, 5), 1, 30);
        await tracking.LogBonusAsync(null, "Walk", new DateOnly(2026, 12, 2), 20);
        var removed = await tracking.LogBonusAsync(null, "Oops", new DateOnly(2026, 12, 2), null);
        await tracking.RemoveBonusAsync(removed.Id);
        await CreateNotesRepository().SetNotesAsync(enrollment.Id, [new(null, null, "note")]);
        await CreateMetricsRepository().SetValuesAsync(MetricTarget.ForSession(enrollment.Id, 0, 0, "w"), new Dictionary<string, MetricValue> { ["m"] = new(1) });
        await CreateCustomPrograms().SaveNewRevisionAsync("{\"v\":1}");
        User.CurrentUserId = "someone-else";
        await tracking.EnrollAsync("q", 1, "t", new DateOnly(2026, 12, 1));
        User.CurrentUserId = null;

        await new SqliteLocalDataClaim(Db, Time, Device).ClaimAsync("alice");

        var connection = await Db.GetConnectionAsync();
        (await connection.Table<EnrollmentEntity>().ToListAsync()).Select(e => e.UserId).Should().BeEquivalentTo(["alice", "someone-else"]);
        (await connection.Table<ScheduleShiftEntity>().ToListAsync()).Should().OnlyContain(s => s.UserId == "alice");
        (await connection.Table<NoteEntity>().ToListAsync()).Should().ContainSingle().Which.UserId.Should().Be("alice");
        (await connection.Table<MetricEntryEntity>().ToListAsync()).Should().ContainSingle().Which.UserId.Should().Be("alice");
        (await connection.Table<CustomProgramEntity>().ToListAsync()).Should().ContainSingle().Which.UserId.Should().Be("alice");
        var bonuses = await connection.Table<BonusWorkoutEntity>().ToListAsync();
        bonuses.Single(b => b.Id == removed.Id).UserId.Should().BeNull("a tombstone that was never pushed has nothing to tell the server");
        bonuses.Single(b => b.Id != removed.Id).UserId.Should().Be("alice");
    }

    [TestMethod]
    public async Task Claim_StampsAfterTheRowsOwnTimeInWholeMicroseconds()
    {
        var enrollment = await CreateRepository().EnrollAsync("p", 1, "t", new DateOnly(2026, 12, 1));
        var connection = await Db.GetConnectionAsync();
        // A row stamped ahead of this clock (another clock, or this one went back) still moves forward.
        var ahead = Time.GetUtcNow().AddHours(1).AddTicks(3);
        await connection.ExecuteAsync("""UPDATE "EnrollmentEntity" SET "UpdatedAt" = ?""", ahead.UtcTicks);

        await new SqliteLocalDataClaim(Db, Time, Device).ClaimAsync("alice");

        var row = await connection.FindAsync<EnrollmentEntity>(enrollment.Id);
        row.UpdatedAt.Should().BeAfter(ahead);
        (row.UpdatedAt.UtcTicks % 10).Should().Be(0);
        row.CreatedAt.Should().Be(enrollment.CreatedAt, "claiming isn't creating");
    }

    [TestMethod]
    public async Task Claim_Settings_KeepTheirOwnTimeSoTheAccountsNewerOnesWin()
    {
        await CreateSettings().SetAsync("MarkerColor", "2");
        var connection = await Db.GetConnectionAsync();
        var before = (await connection.Table<SyncedSettingEntity>().ToListAsync()).Single().UpdatedAt;
        Time.Advance(TimeSpan.FromHours(1));

        await new SqliteLocalDataClaim(Db, Time, Device).ClaimAsync("alice");

        var row = (await connection.Table<SyncedSettingEntity>().ToListAsync()).Single();
        row.UserId.Should().Be("alice");
        row.UpdatedAt.Should().Be(before);
    }

    [TestMethod]
    public async Task GetAnonymousProgress_CountsProgramsCrossesAndTheRest()
    {
        var tracking = CreateRepository();
        var enrollment = await tracking.EnrollAsync("p", 1, "t", new DateOnly(2026, 12, 1));
        await tracking.CrossSessionAsync(enrollment.Id, 0, 0, new DateOnly(2026, 12, 1));
        await tracking.CrossSessionAsync(enrollment.Id, 1, 0, new DateOnly(2026, 12, 2));
        var programs = CreateCustomPrograms();
        var (id, _) = await programs.SaveNewRevisionAsync("{\"v\":1}");
        await programs.SaveNewRevisionAsync("{\"v\":2}", id);
        await CreateReminders().AddAsync(420, 127);

        var progress = await new SqliteLocalDataClaim(Db, Time, Device).GetAnonymousProgressAsync();

        progress.Should().Be(new AnonymousProgress(Programs: 1, CrossedWorkouts: 2, OtherEntries: 2), "a custom program counts once, whatever its revisions");
    }

    [TestMethod]
    public async Task GetAnonymousProgress_SettingsOrOtherAccountsOnly_IsEmpty()
    {
        await CreateSettings().SetAsync("UnitSystem", "0");
        User.CurrentUserId = "someone-else";
        await CreateReminders().AddAsync(420, 127);
        User.CurrentUserId = null;
        var claim = new SqliteLocalDataClaim(Db, Time, Device);

        (await claim.GetAnonymousProgressAsync()).IsEmpty.Should().BeTrue();
        await CreateNotesRepository().SetNotesAsync(Guid.NewGuid(), [new(null, null, "note")]);
        (await claim.GetAnonymousProgressAsync()).IsEmpty.Should().BeFalse();
    }

    [TestMethod]
    public void SyncedTables_CoverEverySyncableEntity()
    {
        var entities = typeof(SyncableEntity).Assembly.GetTypes()
            .Where(t => t.IsSubclassOf(typeof(SyncableEntity)) && !t.IsAbstract)
            .Select(t => t.Name);

        SqliteLocalDataClaim.SyncedTables.Should().BeEquivalentTo(entities, "a new synced table must be claimable too");
    }
}
