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
