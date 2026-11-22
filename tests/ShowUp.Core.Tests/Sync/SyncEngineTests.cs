using System.Text.Json.Nodes;
using AwesomeAssertions;
using ShowUp.Core.Storage;
using ShowUp.Core.Supabase;
using ShowUp.Core.Sync;
using ShowUp.Core.Tracking;

namespace ShowUp.Core.Tests.Sync;

[TestClass]
public sealed class SyncEngineTests
{
    private static readonly DateOnly _today = DateOnly.FromDateTime(SyncWorld.Start.UtcDateTime);

    private SyncWorld _world = null!;

    [TestInitialize]
    public void Init() => _world = new();

    [TestCleanup]
    public async Task Cleanup() => await _world.DisposeAsync();

    [TestMethod]
    public void Tables_CoverEverySyncedTable()
    {
        var phone = _world.AddDevice("phone");

        phone.Engine.Tables.Select(t => t.LocalName).Should().BeEquivalentTo(SqliteLocalDataClaim.SyncedTables);
    }

    [TestMethod]
    public async Task Sync_TwoDevicesCrossSameSessionOffline_EachShowsOneCross()
    {
        var (phone, laptop, enrollment) = await TwoDevicesWithEnrollmentAsync();

        await phone.Tracking.CrossSessionAsync(enrollment.Id, 0, 0, _today);
        await laptop.Tracking.CrossSessionAsync(enrollment.Id, 0, 0, _today);
        await phone.SyncAsync();
        await laptop.SyncAsync();
        await phone.SyncAsync();

        (await phone.AllRowsAsync<CompletionEntity>()).Count(c => c.DeletedAt is null).Should().Be(2, "both rows are kept");
        (await phone.Tracking.GetCompletionsAsync(enrollment.Id)).Should().ContainSingle();
        (await laptop.Tracking.GetCompletionsAsync(enrollment.Id)).Should().ContainSingle();
        (await phone.Tracking.GetCompletionsAsync(enrollment.Id)).Single().CompletionId
            .Should().Be((await laptop.Tracking.GetCompletionsAsync(enrollment.Id)).Single().CompletionId, "every device picks the same row");
    }

    [TestMethod]
    public async Task Sync_UncrossAfterDuplicateCrosses_TombstonesBothEverywhere()
    {
        var (phone, laptop, enrollment) = await TwoDevicesWithEnrollmentAsync();
        await phone.Tracking.CrossSessionAsync(enrollment.Id, 0, 0, _today);
        await laptop.Tracking.CrossSessionAsync(enrollment.Id, 0, 0, _today);
        await phone.SyncAsync();
        await laptop.SyncAsync();

        await laptop.Tracking.UncrossSessionAsync(enrollment.Id, 0, 0);
        await laptop.SyncAsync();
        await phone.SyncAsync();

        (await phone.Tracking.GetCompletionsAsync(enrollment.Id)).Should().BeEmpty();
        (await phone.AllRowsAsync<CompletionEntity>()).Should().HaveCount(2).And.OnlyContain(c => c.DeletedAt != null);
        _world.Server.Rows("completions").Should().HaveCount(2).And.OnlyContain(r => r["deleted_at"] != null);
    }

    [TestMethod]
    public async Task Sync_ClockTenMinutesAhead_LaterEditElsewhereStillWins()
    {
        var phone = _world.AddDevice("phone", skew: TimeSpan.FromMinutes(10));
        var laptop = _world.AddDevice("laptop");
        var reminder = await phone.Reminders.AddAsync(60, 1);
        await phone.SyncAsync();
        await laptop.SyncAsync();
        _world.Advance(TimeSpan.FromMinutes(1));

        await laptop.Reminders.UpdateAsync(reminder.Id, 90, 1);
        await laptop.SyncAsync();
        await phone.SyncAsync();

        (await phone.Reminders.ListAsync()).Single().MinutesOfDay.Should().Be(90);
        (await laptop.Reminders.ListAsync()).Single().MinutesOfDay.Should().Be(90);
    }

    [TestMethod]
    public async Task Sync_ClockTenMinutesBehind_RowsStillArriveByServerTime()
    {
        var phone = _world.AddDevice("phone");
        var laptop = _world.AddDevice("laptop", skew: TimeSpan.FromMinutes(-10));
        await phone.Reminders.AddAsync(60, 1);
        await phone.SyncAsync();
        await laptop.SyncAsync();
        _world.Advance(TimeSpan.FromSeconds(30));

        await laptop.Reminders.AddAsync(120, 2);
        await laptop.SyncAsync();
        await phone.SyncAsync();

        (await phone.Reminders.ListAsync()).Select(r => r.MinutesOfDay).Should().Equal(60, 120);
    }

    [TestMethod]
    public async Task Sync_TokenExpiresMidPush_UnsentRowsStayPending()
    {
        var phone = _world.AddDevice("phone");
        await phone.AddRemindersAsync(250);
        phone.Rest.Fault = (op, _, n) => op == "upsert" && n == 2
            ? new SupabaseException(SupabaseErrorKind.Unauthorized, "JWT expired", 401, "PGRST303")
            : null;

        var act = phone.SyncAsync;

        (await act.Should().ThrowAsync<SupabaseException>()).Which.Kind.Should().Be(SupabaseErrorKind.Unauthorized);
        (await phone.Engine.CountPendingAsync("alice")).Should().Be(50);
        _world.Server.Rows("reminders").Should().HaveCount(200);

        phone.Rest.Fault = null;
        await phone.SyncAsync();

        (await phone.Engine.CountPendingAsync("alice")).Should().Be(0);
        _world.Server.Rows("reminders").Should().HaveCount(250);
    }

    [TestMethod]
    public async Task Sync_PushesInBatchesOf200()
    {
        var phone = _world.AddDevice("phone");
        await phone.AddRemindersAsync(450);

        await phone.SyncAsync();

        phone.Rest.UpsertSizes.Should().Equal(200, 200, 50);
    }

    [TestMethod]
    public async Task Sync_PushedRows_SendEveryKeyButServerTime()
    {
        var phone = _world.AddDevice("phone");
        await phone.Reminders.AddAsync(60, 1);

        await phone.SyncAsync();

        var body = JsonNode.Parse(phone.Rest.UpsertBodies.Single())!.AsObject();
        body.Select(p => p.Key).Should().BeEquivalentTo(
            ["id", "user_id", "device_id", "created_at", "updated_at", "deleted_at", "minutes_of_day", "days_mask", "enabled"]);
        body["deleted_at"].Should().BeNull();
        body["updated_at"]!.GetValue<string>().Should().Be("2026-12-10T12:00:00.000000Z");
    }

    [TestMethod]
    public async Task Sync_Tombstone_PropagatesToTheOtherDevice()
    {
        var phone = _world.AddDevice("phone");
        var laptop = _world.AddDevice("laptop");
        var reminder = await phone.Reminders.AddAsync(60, 1);
        await phone.SyncAsync();
        await laptop.SyncAsync();
        _world.Advance(TimeSpan.FromSeconds(10));

        await phone.Reminders.DeleteAsync(reminder.Id);
        await phone.SyncAsync();
        await laptop.SyncAsync();

        (await laptop.Reminders.ListAsync()).Should().BeEmpty();
        (await laptop.AllRowsAsync<ReminderEntity>()).Single().DeletedAt.Should().NotBeNull();
    }

    [TestMethod]
    public async Task Sync_StalePush_TakesTheServerWinnerAndClearsPending()
    {
        var phone = _world.AddDevice("phone");
        var laptop = _world.AddDevice("laptop", skew: TimeSpan.FromMinutes(-10));
        var reminder = await phone.Reminders.AddAsync(60, 1);
        await phone.SyncAsync();
        await laptop.SyncAsync();
        _world.Advance(TimeSpan.FromMinutes(1));
        await phone.Reminders.UpdateAsync(reminder.Id, 90, 1);
        await phone.SyncAsync();

        // The laptop's clock is behind: its edit (not having seen the phone's) is older.
        _world.Advance(TimeSpan.FromMinutes(1));
        await laptop.Reminders.UpdateAsync(reminder.Id, 30, 1);
        var result = await laptop.SyncAsync();

        result.Applied.Should().BeGreaterThan(0);
        (await laptop.Reminders.ListAsync()).Single().MinutesOfDay.Should().Be(90);
        (await laptop.Engine.CountPendingAsync("alice")).Should().Be(0);
    }

    [TestMethod]
    public async Task Pull_PendingLocalEditNewer_IsNotOverwritten()
    {
        var phone = _world.AddDevice("phone");
        var laptop = _world.AddDevice("laptop");
        var reminder = await phone.Reminders.AddAsync(60, 1);
        await phone.SyncAsync();
        await laptop.SyncAsync();
        _world.Advance(TimeSpan.FromMinutes(1));
        await phone.Reminders.UpdateAsync(reminder.Id, 90, 1);
        await phone.SyncAsync();
        _world.Advance(TimeSpan.FromMinutes(1));
        await laptop.Reminders.UpdateAsync(reminder.Id, 30, 1);

        var reminders = laptop.Engine.Tables.Single(t => t.LocalName == nameof(ReminderEntity));
        await reminders.PullAsync(await laptop.Db.GetConnectionAsync(), laptop.Rest, new SyncPass("alice"));

        (await laptop.Reminders.ListAsync()).Single().MinutesOfDay.Should().Be(30);
        (await laptop.AllRowsAsync<ReminderEntity>()).Single().SyncPending.Should().BeTrue();
    }

    [TestMethod]
    public async Task Pull_CursorOnlyMovesForward()
    {
        var phone = _world.AddDevice("phone");
        var laptop = _world.AddDevice("laptop");
        await phone.Reminders.AddAsync(60, 1);
        _world.Advance(TimeSpan.FromSeconds(1));
        await phone.SyncAsync();
        await laptop.SyncAsync();
        long first = await laptop.CursorAsync(nameof(ReminderEntity));

        // A late commit lands below the cursor; re-reading it must not pull the cursor back.
        var late = _world.Server.ClientFor(() => "alice", () => SyncWorld.Start);
        await late.UpsertAsync("reminders", [Reminder("alice", 300)], SyncJsonContext.Default.ReminderRow);
        await laptop.SyncAsync();

        first.Should().Be(SyncWorld.Start.AddSeconds(1).UtcTicks);
        (await laptop.CursorAsync(nameof(ReminderEntity))).Should().Be(first);
    }

    [TestMethod]
    public async Task Pull_LateCommitBelowCursor_IsCaughtByTheOverlap()
    {
        var phone = _world.AddDevice("phone");
        var laptop = _world.AddDevice("laptop");
        DateTimeOffset lateStart = _world.ServerClock.GetUtcNow();
        var late = _world.Server.ClientFor(() => "alice", () => lateStart);
        _world.Advance(TimeSpan.FromSeconds(2));
        await phone.Reminders.AddAsync(60, 1);
        await phone.SyncAsync();
        await laptop.SyncAsync();

        await late.UpsertAsync("reminders", [Reminder("alice", 300)], SyncJsonContext.Default.ReminderRow);
        await laptop.SyncAsync();

        (await laptop.Reminders.ListAsync()).Select(r => r.MinutesOfDay).Should().Equal(60, 300);
    }

    [TestMethod]
    public async Task Pull_FailsOnSecondPage_KeepsTheFirstAndResumes()
    {
        var phone = _world.AddDevice("phone");
        var laptop = _world.AddDevice("laptop");
        await phone.AddRemindersAsync(500);
        await phone.SyncAsync();
        _world.Advance(TimeSpan.FromSeconds(30));
        await phone.AddRemindersAsync(300);
        await phone.SyncAsync();
        laptop.Rest.Fault = (op, table, n) => op == "select" && table == "reminders" && n == 2 ? new SupabaseException(SupabaseErrorKind.Network, "offline") : null;

        var act = laptop.SyncAsync;

        await act.Should().ThrowAsync<SupabaseException>();
        (await laptop.AllRowsAsync<ReminderEntity>()).Should().HaveCount(500);
        (await laptop.CursorAsync(nameof(ReminderEntity))).Should().Be(SyncWorld.Start.UtcTicks);

        laptop.Rest.Fault = null;
        await laptop.SyncAsync();

        (await laptop.AllRowsAsync<ReminderEntity>()).Should().HaveCount(800);
        (await laptop.CursorAsync(nameof(ReminderEntity))).Should().Be(SyncWorld.Start.AddSeconds(30).UtcTicks);
    }

    [TestMethod]
    public async Task Pull_AccountSwitchesMidPass_LeavesTheOldAccountUntouched()
    {
        var tablet = _world.AddDevice("tablet", userId: "bob");
        await tablet.Reminders.AddAsync(300, 1);
        await tablet.SyncAsync();
        _world.Advance(TimeSpan.FromSeconds(10));
        var laptop = _world.AddDevice("laptop");
        await laptop.Reminders.AddAsync(60, 1);
        await laptop.SyncAsync();
        var phone = _world.AddDevice("phone");
        phone.Rest.Fault = (op, table, n) =>
        {
            // Signed in as Bob while Alice's pull is out: the request goes with Bob's token.
            if (op == "select" && table == "reminders" && n == 1)
            {
                phone.User.CurrentUserId = "bob";
            }

            return null;
        };

        var act = phone.SyncAsync;

        await act.Should().ThrowAsync<OperationCanceledException>();
        (await phone.CursorAsync(nameof(ReminderEntity), "alice")).Should().Be(0);
        (await phone.AllRowsAsync<ReminderEntity>()).Should().BeEmpty("Bob's rows were not written as Alice's pull");

        phone.Rest.Fault = null;
        phone.User.CurrentUserId = "alice";
        await phone.SyncAsync();

        (await phone.Reminders.ListAsync()).Select(r => r.MinutesOfDay).Should().Equal(60);
    }

    [TestMethod]
    public async Task Push_AccountSwitchesMidPass_KeepsTheRowsPending()
    {
        var phone = _world.AddDevice("phone");
        await phone.Reminders.AddAsync(60, 1);
        phone.Rest.Fault = (op, _, _) =>
        {
            if (op == "upsert")
            {
                phone.User.CurrentUserId = "bob";
            }

            return null;
        };

        var act = phone.SyncAsync;

        await act.Should().ThrowAsync<Exception>();
        _world.Server.Rows("reminders").Should().BeEmpty();
        (await phone.Engine.CountPendingAsync("alice")).Should().Be(1);
    }

    [TestMethod]
    public async Task Pull_ForeignUsersRow_IsSkippedAndDoesNotMoveTheCursor()
    {
        var phone = _world.AddDevice("phone");
        var laptop = _world.AddDevice("laptop");
        await phone.Reminders.AddAsync(60, 1);
        await phone.SyncAsync();
        laptop.Rest.ExtraRows.Add(new ReminderRow
        {
            Id = Guid.NewGuid(),
            UserId = "mallory",
            DeviceId = "elsewhere",
            CreatedAt = SyncWorld.Start,
            UpdatedAt = SyncWorld.Start,
            ServerUpdatedAt = SyncWorld.Start.AddHours(1),
            MinutesOfDay = 999,
            DaysMask = 1,
        });

        await laptop.SyncAsync();

        (await laptop.AllRowsAsync<ReminderEntity>()).Select(r => r.MinutesOfDay).Should().Equal(60);
        (await laptop.CursorAsync(nameof(ReminderEntity))).Should().Be(SyncWorld.Start.UtcTicks);
    }

    [TestMethod]
    public async Task Push_OnlySendsTheSignedInUsersRows()
    {
        var phone = _world.AddDevice("phone", userId: "alice");
        await phone.Reminders.AddAsync(60, 1);
        phone.User.CurrentUserId = "bob";
        await phone.Reminders.AddAsync(120, 2);

        await phone.SyncAsync();

        _world.Server.Rows("reminders").Should().ContainSingle().Which["user_id"]!.GetValue<string>().Should().Be("bob");
        (await phone.Engine.CountPendingAsync("alice")).Should().Be(1, "Alice's row waits for Alice");
    }

    [TestMethod]
    public async Task Sync_ClaimedSettings_UploadButLoseToTheAccountsNewerValue()
    {
        var laptop = _world.AddDevice("laptop", userId: null);
        await laptop.Settings.SetAsync("MarkerColor", "red");
        _world.Advance(TimeSpan.FromMinutes(5));
        var phone = _world.AddDevice("phone", skew: TimeSpan.FromMinutes(5));
        await phone.Settings.SetAsync("MarkerColor", "blue");
        await phone.SyncAsync();
        _world.Advance(TimeSpan.FromMinutes(1));

        await new SqliteLocalDataClaim(laptop.Db, laptop.Clock, laptop.DeviceId).ClaimAsync("alice");
        laptop.User.CurrentUserId = "alice";
        await laptop.SyncAsync();
        await phone.SyncAsync();

        _world.Server.Rows("synced_settings").Should().HaveCount(2, "the claimed setting was uploaded");
        (await laptop.Settings.GetAsync("MarkerColor")).Should().Be("blue");
        (await phone.Settings.GetAsync("MarkerColor")).Should().Be("blue");
        (await laptop.Engine.CountPendingAsync("alice")).Should().Be(0);
    }

    [TestMethod]
    public async Task Sync_NothingChanged_AppliesNothing()
    {
        var phone = _world.AddDevice("phone");
        var laptop = _world.AddDevice("laptop");
        await phone.Reminders.AddAsync(60, 1);
        await phone.SyncAsync();
        (await laptop.SyncAsync()).Applied.Should().Be(1);

        var again = await laptop.SyncAsync();

        again.Should().Be(new SyncResult(0, 0), "the overlap re-reads rows it already has");
    }

    [TestMethod]
    public async Task Sync_EditDuringPush_StaysPending()
    {
        var phone = _world.AddDevice("phone");
        var reminder = await phone.Reminders.AddAsync(60, 1);
        phone.Rest.Fault = (op, _, _) =>
        {
            if (op == "upsert")
            {
                phone.Reminders.SetEnabledAsync(reminder.Id, false).GetAwaiter().GetResult();
            }

            return null;
        };

        await phone.SyncAsync();

        (await phone.Engine.CountPendingAsync("alice")).Should().Be(1);
    }

    private async Task<(SimDevice Phone, SimDevice Laptop, EnrollmentEntity Enrollment)> TwoDevicesWithEnrollmentAsync()
    {
        var phone = _world.AddDevice("phone");
        var laptop = _world.AddDevice("laptop");
        var enrollment = await phone.Tracking.EnrollAsync("insanity", 1, "main", _today);
        await phone.SyncAsync();
        await laptop.SyncAsync();
        _world.Advance(TimeSpan.FromMinutes(1));
        return (phone, laptop, enrollment);
    }

    private static ReminderRow Reminder(string userId, int minutes) => new()
    {
        Id = Guid.NewGuid(),
        UserId = userId,
        DeviceId = "tablet",
        CreatedAt = SyncWorld.Start,
        UpdatedAt = SyncWorld.Start,
        MinutesOfDay = minutes,
        DaysMask = 1,
        Enabled = true,
    };
}
