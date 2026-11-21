using AwesomeAssertions;
using ShowUp.Core.Storage;

namespace ShowUp.Core.Tests.Storage;

[TestClass]
public class ProStorageRepositoriesTests : StorageTestBase
{
    [TestMethod]
    public async Task Reminders_AddListUpdateToggle_RoundTrip()
    {
        var reminders = CreateReminders();
        var late = await reminders.AddAsync(20 * 60, 0b0011111);
        var early = await reminders.AddAsync(7 * 60, 0b1100000, enabled: false);
        Time.Advance(TimeSpan.FromMinutes(1));

        await reminders.UpdateAsync(late.Id, 19 * 60, 0b1111111);
        await reminders.SetEnabledAsync(early.Id, true);

        var all = await reminders.ListAsync();
        all.Select(r => (r.MinutesOfDay, r.DaysMask, r.Enabled)).Should().Equal((7 * 60, 0b1100000, true), (19 * 60, 0b1111111, true));
        all.Should().OnlyContain(r => r.DeviceId == "device-1" && r.UpdatedAt == Time.GetUtcNow());
    }

    [TestMethod]
    public async Task Reminders_Delete_LeavesATombstone()
    {
        var reminders = CreateReminders();
        var reminder = await reminders.AddAsync(480, 1);

        await reminders.DeleteAsync(reminder.Id);

        (await reminders.ListAsync()).Should().BeEmpty();
        var connection = await Db.GetConnectionAsync();
        (await connection.Table<ReminderEntity>().ToListAsync()).Should().ContainSingle().Which.DeletedAt.Should().NotBeNull();
    }

    [TestMethod]
    public async Task Reminders_OtherUser_AreNotVisibleOrEditable()
    {
        var reminders = CreateReminders();
        var mine = await reminders.AddAsync(480, 1);
        User.CurrentUserId = "someone-else";

        await reminders.SetEnabledAsync(mine.Id, false);
        await reminders.DeleteAsync(mine.Id);

        (await reminders.ListAsync()).Should().BeEmpty();
        User.CurrentUserId = null;
        (await reminders.ListAsync()).Should().ContainSingle().Which.Enabled.Should().BeTrue();
    }

    [TestMethod]
    public async Task CustomPrograms_SaveRevisions_LatestIsCurrentOldOnesResolve()
    {
        var programs = CreateCustomPrograms();

        var (id, first) = await programs.SaveNewRevisionAsync("{\"v\":1}");
        var (sameId, second) = await programs.SaveNewRevisionAsync("{\"v\":2}", id);

        id.Should().StartWith("custom-");
        (sameId, first, second).Should().Be((id, Stamp(), Stamp() + 1));
        (await programs.GetAsync(id))!.ProgramJson.Should().Be("{\"v\":2}");
        (await programs.GetAsync(id, first))!.ProgramJson.Should().Be("{\"v\":1}");
        (await programs.GetAsync(id, second + 1)).Should().BeNull();
        var all = await programs.GetAllAsync();
        all.Should().ContainSingle().Which.Revision.Should().Be(second);
        all[0].ProgramId.Should().Be(id);
    }

    [TestMethod]
    public async Task CustomPrograms_GetRevisions_ListsEveryRevisionOldestFirstEvenWhenDeleted()
    {
        var programs = CreateCustomPrograms();
        var (id, first) = await programs.SaveNewRevisionAsync("{\"v\":1}");
        var (_, second) = await programs.SaveNewRevisionAsync("{\"v\":2}", id);
        await programs.DeleteAsync(id);

        (await programs.GetRevisionsAsync(id)).Should().Equal(first, second);
        (await programs.GetRevisionsAsync("custom-missing")).Should().BeEmpty();
    }

    [TestMethod]
    public async Task CustomPrograms_Delete_HidesButPinnedRevisionResolves()
    {
        var programs = CreateCustomPrograms();
        var (id, first) = await programs.SaveNewRevisionAsync("a");
        var (_, second) = await programs.SaveNewRevisionAsync("b", id);
        Time.Advance(TimeSpan.FromMinutes(1));

        await programs.DeleteAsync(id);

        (await programs.GetAllAsync()).Should().BeEmpty();
        (await programs.GetAsync(id)).Should().BeNull();
        (await programs.GetAsync(id, first))!.ProgramJson.Should().Be("a");
        (await programs.GetAsync(id, second))!.ProgramJson.Should().Be("b");
        var connection = await Db.GetConnectionAsync();
        (await connection.Table<CustomProgramEntity>().ToListAsync()).Should().HaveCount(2).And.OnlyContain(p => p.DeletedAt == null);
    }

    [TestMethod]
    public async Task CustomPrograms_OtherUser_AreNotVisible()
    {
        var programs = CreateCustomPrograms();
        var (id, _) = await programs.SaveNewRevisionAsync("a");
        User.CurrentUserId = "someone-else";

        (await programs.GetAllAsync()).Should().BeEmpty();
        (await programs.GetAsync(id)).Should().BeNull();
        var (_, revision) = await programs.SaveNewRevisionAsync("b", id);
        revision.Should().Be(Stamp());
    }

    [TestMethod]
    public async Task CustomPrograms_TwoDevicesSameRevision_ReadsTheNewest()
    {
        var connection = await Db.GetConnectionAsync();
        await connection.InsertAsync(Program("c:custom-x", 1, "phone", Time.GetUtcNow()));
        await connection.InsertAsync(Program("c:custom-x", 1, "laptop", Time.GetUtcNow().AddMinutes(2)));

        var program = await CreateCustomPrograms().GetAsync("custom-x", 1);

        program!.ProgramJson.Should().Be("laptop");
        (await CreateCustomPrograms().GetAllAsync()).Should().ContainSingle();
    }

    [TestMethod]
    public async Task Settings_SetGet_UpsertsInPlace()
    {
        var settings = CreateSettings();
        await settings.SetAsync("marker", "red");
        Time.Advance(TimeSpan.FromMinutes(1));

        await settings.SetAsync("marker", "blue");

        (await settings.GetAsync("marker")).Should().Be("blue");
        (await settings.GetAsync("other")).Should().BeNull();
        var connection = await Db.GetConnectionAsync();
        var row = (await connection.Table<SyncedSettingEntity>().ToListAsync()).Should().ContainSingle().Subject;
        (row.SubjectKey, row.UpdatedAt, row.DeviceId).Should().Be(("k:marker", Time.GetUtcNow(), "device-1"));
    }

    [TestMethod]
    public async Task Settings_OtherUser_AreNotVisibleAndDoNotClash()
    {
        var settings = CreateSettings();
        await settings.SetAsync("marker", "red");
        User.CurrentUserId = "someone-else";

        (await settings.GetAsync("marker")).Should().BeNull();
        await settings.SetAsync("marker", "green");

        User.CurrentUserId = null;
        (await settings.GetAsync("marker")).Should().Be("red");
    }

    [TestMethod]
    public async Task Settings_TwoDevicesSameKey_ReadsTheNewest()
    {
        var connection = await Db.GetConnectionAsync();
        await connection.InsertAsync(Setting("phone", Time.GetUtcNow().AddMinutes(3)));
        await connection.InsertAsync(Setting("laptop", Time.GetUtcNow()));

        (await CreateSettings().GetAsync("marker")).Should().Be("phone");
    }

    [TestMethod]
    public async Task Settings_TwoDevicesSameKey_NextSetCollapsesThem()
    {
        var connection = await Db.GetConnectionAsync();
        await connection.InsertAsync(Setting("phone", Time.GetUtcNow()));
        await connection.InsertAsync(Setting("laptop", Time.GetUtcNow().AddMinutes(1)));

        await CreateSettings().SetAsync("marker", "merged");

        (await connection.Table<SyncedSettingEntity>().ToListAsync()).Count(s => s.DeletedAt is null).Should().Be(1);
        (await CreateSettings().GetAsync("marker")).Should().Be("merged");
    }

    [TestMethod]
    public async Task CustomPrograms_TwoDevicesOffline_ProduceDistinctRevisions()
    {
        var phone = CreateCustomPrograms();
        var (id, shared) = await phone.SaveNewRevisionAsync("base");
        Device.DeviceId = "laptop";
        Time.Advance(TimeSpan.FromSeconds(30));
        var (_, laptopRevision) = await phone.SaveNewRevisionAsync("laptop edit", id);
        Device.DeviceId = "phone";
        Time.Advance(TimeSpan.FromSeconds(30));

        var (_, phoneRevision) = await phone.SaveNewRevisionAsync("phone edit", id);

        new[] { shared, laptopRevision, phoneRevision }.Should().OnlyHaveUniqueItems().And.BeInAscendingOrder();
    }

    [TestMethod]
    public async Task CustomPrograms_PinnedOldRevision_ResolvesAfterOtherDevicesRevisionArrives()
    {
        var programs = CreateCustomPrograms();
        var (id, pinned) = await programs.SaveNewRevisionAsync("v1");
        var connection = await Db.GetConnectionAsync();
        await connection.InsertAsync(Program("c:" + id, pinned + 500, "from the laptop", Time.GetUtcNow().AddMinutes(5)));

        (await programs.GetAsync(id, pinned))!.ProgramJson.Should().Be("v1");
        (await programs.GetAsync(id))!.ProgramJson.Should().Be("from the laptop");
    }

    [TestMethod]
    public async Task Settings_TieOnUpdatedAt_HigherIdWinsOnEveryDevice()
    {
        var connection = await Db.GetConnectionAsync();
        var at = Time.GetUtcNow();
        var low = Setting("low", at);
        low.Id = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var high = Setting("high", at);
        high.Id = Guid.Parse("ffffffff-0000-0000-0000-000000000001");
        await connection.InsertAsync(high);
        await connection.InsertAsync(low);

        (await CreateSettings().GetAsync("marker")).Should().Be("high");
    }

    [TestMethod]
    public async Task CustomPrograms_TieOnUpdatedAt_HigherIdWins()
    {
        var connection = await Db.GetConnectionAsync();
        var at = Time.GetUtcNow();
        var low = Program("c:custom-x", 1, "low", at);
        low.Id = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var high = Program("c:custom-x", 1, "high", at);
        high.Id = Guid.Parse("ffffffff-0000-0000-0000-000000000001");
        await connection.InsertAsync(low);
        await connection.InsertAsync(high);

        (await CreateCustomPrograms().GetAsync("custom-x", 1))!.ProgramJson.Should().Be("high");
    }

    [TestMethod]
    public async Task CustomPrograms_Deleted_StaysHiddenWhenOlderRevisionSyncsIn()
    {
        var programs = CreateCustomPrograms();
        var (id, first) = await programs.SaveNewRevisionAsync("a");
        Time.Advance(TimeSpan.FromMinutes(1));
        await programs.DeleteAsync(id);
        var connection = await Db.GetConnectionAsync();
        await connection.InsertAsync(Program("c:" + id, first + 5, "late arrival", Time.GetUtcNow().AddSeconds(-30)));

        (await programs.GetAllAsync()).Should().BeEmpty();
        (await programs.GetAsync(id)).Should().BeNull();
    }

    [TestMethod]
    public async Task CustomPrograms_Deleted_RevisionSavedAfterTheMarkerIsVisible()
    {
        var programs = CreateCustomPrograms();
        var (id, first) = await programs.SaveNewRevisionAsync("a");
        await programs.DeleteAsync(id);
        var connection = await Db.GetConnectionAsync();
        await connection.InsertAsync(Program("c:" + id, first + 5, "newer", Time.GetUtcNow().AddMinutes(1)));

        (await programs.GetAsync(id))!.ProgramJson.Should().Be("newer");
    }

    [TestMethod]
    public async Task CustomPrograms_SaveToDeletedId_Throws()
    {
        var programs = CreateCustomPrograms();
        var (id, _) = await programs.SaveNewRevisionAsync("a");
        await programs.DeleteAsync(id);

        Func<Task> act = () => programs.SaveNewRevisionAsync("again", id);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [TestMethod]
    public async Task CustomPrograms_GetAll_OrdersByCreationAcrossPrograms()
    {
        var programs = CreateCustomPrograms();
        var (first, _) = await programs.SaveNewRevisionAsync("one");
        Time.Advance(TimeSpan.FromMinutes(1));
        var (second, _) = await programs.SaveNewRevisionAsync("two");
        Time.Advance(TimeSpan.FromMinutes(1));
        await programs.SaveNewRevisionAsync("one v2", first);

        (await programs.GetAllAsync()).Select(p => p.ProgramId).Should().Equal(first, second);
    }

    [TestMethod]
    public void CustomProgramEntity_ProgramId_IsNullForMalformedKey()
    {
        new CustomProgramEntity { SubjectKey = "x:custom-1" }.ProgramId.Should().BeNull();
        new CustomProgramEntity { SubjectKey = "c:custom-1" }.ProgramId.Should().Be("custom-1");
    }

    [TestMethod]
    [DataRow(-1, 1)]
    [DataRow(1440, 1)]
    [DataRow(480, 0)]
    [DataRow(480, 128)]
    public async Task Reminders_OutOfRange_Throws(int minutes, int mask)
    {
        var reminders = CreateReminders();
        var existing = await reminders.AddAsync(480, 1);

        Func<Task> add = () => reminders.AddAsync(minutes, mask);
        Func<Task> update = () => reminders.UpdateAsync(existing.Id, minutes, mask);

        await add.Should().ThrowAsync<ArgumentOutOfRangeException>();
        await update.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [TestMethod]
    public async Task CustomPrograms_UnreadableDeleteMarker_HidesProgramAndRefusesSaves()
    {
        var programs = CreateCustomPrograms();
        var (id, _) = await programs.SaveNewRevisionAsync("a");
        await CreateSettings().SetAsync("custom-deleted:" + id, "garbage");

        (await programs.GetAllAsync()).Should().BeEmpty();
        (await programs.GetAsync(id)).Should().BeNull();
        Func<Task> act = () => programs.SaveNewRevisionAsync("again", id);
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [TestMethod]
    public async Task CustomPrograms_DeleteUnknownId_WritesNoMarker()
    {
        await CreateCustomPrograms().DeleteAsync("custom-nope");

        (await CreateSettings().GetAsync("custom-deleted:custom-nope")).Should().BeNull();
    }

    [TestMethod]
    public async Task CustomPrograms_Delete_LeavesOtherProgramsVisible()
    {
        var programs = CreateCustomPrograms();
        var (gone, _) = await programs.SaveNewRevisionAsync("a");
        var (kept, _) = await programs.SaveNewRevisionAsync("b");
        Time.Advance(TimeSpan.FromMinutes(1));

        await programs.DeleteAsync(gone);

        (await programs.GetAllAsync()).Select(p => p.ProgramId).Should().Equal(kept);
    }

    [TestMethod]
    public async Task Settings_GetByPrefix_ReturnsNewestPerKeyForCurrentUser()
    {
        var settings = CreateSettings();
        await settings.SetAsync("p:a", "1");
        await settings.SetAsync("p:b", "2");
        await settings.SetAsync("other", "3");
        await settings.SetAsync("p:a", "4");
        User.CurrentUserId = "someone-else";
        await settings.SetAsync("p:c", "5");
        User.CurrentUserId = null;

        var result = await settings.GetByPrefixAsync("p:");

        result.Should().BeEquivalentTo(new Dictionary<string, string> { ["p:a"] = "4", ["p:b"] = "2" });
    }

    [TestMethod]
    public async Task Settings_GetByPrefix_TreatsLikeWildcardsLiterally()
    {
        var settings = CreateSettings();
        await settings.SetAsync("a_b", "1");
        await settings.SetAsync("axb", "2");
        await settings.SetAsync("100%", "3");
        await settings.SetAsync("100x", "4");

        (await settings.GetByPrefixAsync("a_")).Keys.Should().Equal("a_b");
        (await settings.GetByPrefixAsync("100%")).Keys.Should().Equal("100%");
    }

    private int Stamp() => (int)(Time.GetUtcNow() - new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)).TotalSeconds;

    private CustomProgramEntity Program(string key, int revision, string json, DateTimeOffset updatedAt) => new()
    {
        Id = Guid.NewGuid(),
        SubjectKey = key,
        Revision = revision,
        ProgramJson = json,
        DeviceId = json,
        CreatedAt = Time.GetUtcNow(),
        UpdatedAt = updatedAt,
    };

    private SyncedSettingEntity Setting(string device, DateTimeOffset updatedAt) => new()
    {
        Id = Guid.NewGuid(),
        SubjectKey = "k:marker",
        Key = "marker",
        Value = device,
        DeviceId = device,
        CreatedAt = Time.GetUtcNow(),
        UpdatedAt = updatedAt,
    };
}
