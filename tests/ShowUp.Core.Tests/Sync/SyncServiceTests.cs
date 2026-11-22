using AwesomeAssertions;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging.Abstractions;
using ShowUp.Core.Messages;
using ShowUp.Core.Supabase;
using ShowUp.Core.Sync;
using ShowUp.Core.Tests.Accounts;
using ShowUp.Core.Tests.Entitlements;
using ShowUp.Core.Tests.Tracking;

namespace ShowUp.Core.Tests.Sync;

[TestClass]
public sealed class SyncServiceTests
{
    private SyncWorld _world = null!;
    private readonly FakeEntitlementService _entitlements = new() { IsPro = true };
    private readonly WeakReferenceMessenger _messenger = new();
    private readonly MemoryPreferences _preferences = new();
    private int _announced;
    private bool _available = true;

    [TestInitialize]
    public void Init()
    {
        _world = new();
        _messenger.Register<DataChangedMessage>(this, (_, _) => Interlocked.Increment(ref _announced));
    }

    [TestCleanup]
    public async Task Cleanup() => await _world.DisposeAsync();

    [TestMethod]
    public async Task Start_SignedInWithPro_SyncsAndRemembersWhen()
    {
        var phone = _world.AddDevice("phone");
        await phone.Reminders.AddAsync(60, 1);
        var service = Create(phone);

        await service.StartAsync();
        await service.WaitForIdleAsync();

        service.State.Should().Be(SyncState.Idle);
        service.LastSyncedAt.Should().Be(SyncWorld.Start);
        _world.Server.Rows("reminders").Should().ContainSingle();
    }

    [TestMethod]
    public async Task Start_WithoutPro_IsPausedAndLeavesTheServerAlone()
    {
        _entitlements.IsPro = false;
        var phone = _world.AddDevice("phone");
        await phone.Reminders.AddAsync(60, 1);
        var service = Create(phone);

        await service.StartAsync();

        service.State.Should().Be(SyncState.Paused);
        phone.Rest.Upserts.Should().Be(0);
        (await service.HasUnsyncedChangesAsync()).Should().BeFalse("nothing would sync anyway");
    }

    [TestMethod]
    public async Task ProRegained_SyncsAgain()
    {
        _entitlements.IsPro = false;
        var phone = _world.AddDevice("phone");
        await phone.Reminders.AddAsync(60, 1);
        var service = Create(phone);
        await service.StartAsync();

        _entitlements.SetPro(true);
        await service.WaitForIdleAsync();

        service.State.Should().Be(SyncState.Idle);
        _world.Server.Rows("reminders").Should().ContainSingle();
    }

    [TestMethod]
    public async Task ProLapses_ShowsPaused()
    {
        var phone = _world.AddDevice("phone");
        var service = Create(phone);
        await service.StartAsync();
        await service.WaitForIdleAsync();

        _entitlements.SetPro(false);

        service.State.Should().Be(SyncState.Paused);
    }

    [TestMethod]
    [DataRow(false, null, SyncState.Off)]
    [DataRow(true, null, SyncState.SignedOut)]
    public async Task Start_NotPossible_SaysWhy(bool available, string? user, SyncState expected)
    {
        _available = available;
        var phone = _world.AddDevice("phone", userId: user);
        var service = Create(phone);

        await service.StartAsync();
        await service.WaitForIdleAsync();

        service.State.Should().Be(expected);
        phone.Rest.Selects.Should().Be(0);
    }

    [TestMethod]
    public async Task LocalChange_SyncsAfterTheDebounce()
    {
        var phone = _world.AddDevice("phone");
        var service = Create(phone);
        await service.StartAsync();
        await service.WaitForIdleAsync();

        await phone.Reminders.AddAsync(60, 1);
        phone.Clock.Advance(SyncService.Debounce - TimeSpan.FromSeconds(1));
        await service.WaitForIdleAsync();
        _world.Server.Rows("reminders").Should().BeEmpty();
        phone.Clock.Advance(TimeSpan.FromSeconds(1));
        await service.WaitForIdleAsync();

        _world.Server.Rows("reminders").Should().ContainSingle();
    }

    [TestMethod]
    public async Task PulledChanges_AnnounceOnceAndDoNotSyncAgain()
    {
        var phone = _world.AddDevice("phone");
        await phone.Reminders.AddAsync(60, 1);
        await phone.Reminders.AddAsync(120, 1);
        await phone.SyncAsync();
        var laptop = _world.AddDevice("laptop");
        var service = Create(laptop);

        await service.StartAsync();
        await service.WaitForIdleAsync();
        int selects = laptop.Rest.Selects;
        laptop.Clock.Advance(TimeSpan.FromMinutes(1));
        await service.WaitForIdleAsync();

        _announced.Should().Be(1);
        laptop.Rest.Selects.Should().Be(selects, "rows the sync applied are not local changes");
        (await laptop.Reminders.ListAsync()).Should().HaveCount(2);
    }

    [TestMethod]
    public async Task Offline_ShowsOfflineAndRetriesWithBackoff()
    {
        var phone = _world.AddDevice("phone");
        await phone.Reminders.AddAsync(60, 1);
        phone.Rest.Fault = (_, _, _) => new SupabaseException(SupabaseErrorKind.Network, "offline");
        var service = Create(phone);

        await service.StartAsync();
        await service.WaitForIdleAsync();
        service.State.Should().Be(SyncState.Offline);
        int calls = phone.Rest.Upserts;

        phone.Clock.Advance(SyncService.Backoff[0] - TimeSpan.FromSeconds(1));
        await service.WaitForIdleAsync();
        phone.Rest.Upserts.Should().Be(calls);
        phone.Rest.Fault = null;
        phone.Clock.Advance(TimeSpan.FromSeconds(1));
        await service.WaitForIdleAsync();

        service.State.Should().Be(SyncState.Idle);
        _world.Server.Rows("reminders").Should().ContainSingle();
    }

    [TestMethod]
    public async Task Unauthorized_AsksToSignInAgainAndKeepsTheRows()
    {
        var phone = _world.AddDevice("phone");
        await phone.Reminders.AddAsync(60, 1);
        phone.Rest.Fault = (_, _, _) => new SupabaseException(SupabaseErrorKind.Unauthorized, "JWT expired", 401);
        var service = Create(phone);

        await service.StartAsync();
        await service.WaitForIdleAsync();
        phone.Clock.Advance(TimeSpan.FromHours(1));
        await service.WaitForIdleAsync();

        service.State.Should().Be(SyncState.SignInAgain);
        (phone.Rest.Upserts + phone.Rest.Selects).Should().Be(1, "no retry until the user signs in again");
        (await service.HasUnsyncedChangesAsync()).Should().BeTrue();
    }

    [TestMethod]
    public async Task SyncNow_AfterOffline_RunsAtOnce()
    {
        var phone = _world.AddDevice("phone");
        await phone.Reminders.AddAsync(60, 1);
        phone.Rest.Fault = (_, _, _) => new SupabaseException(SupabaseErrorKind.Network, "offline");
        var service = Create(phone);
        await service.StartAsync();
        await service.WaitForIdleAsync();
        phone.Rest.Fault = null;

        await service.SyncNowAsync();

        service.State.Should().Be(SyncState.Idle);
        (await service.HasUnsyncedChangesAsync()).Should().BeFalse();
    }

    [TestMethod]
    public async Task SignIn_Syncs()
    {
        var phone = _world.AddDevice("phone", userId: null);
        var service = Create(phone);
        await service.StartAsync();
        await service.WaitForIdleAsync();
        phone.User.CurrentUserId = "alice";
        await phone.Reminders.AddAsync(60, 1);

        phone.User.Switch("alice");
        await service.WaitForIdleAsync();

        _world.Server.Rows("reminders").Should().ContainSingle();
    }

    [TestMethod]
    public async Task SyncNow_Concurrent_RunsOneSyncAtATime()
    {
        var phone = _world.AddDevice("phone");
        var service = Create(phone);
        int running = 0;
        int overlap = 0;
        phone.Rest.Fault = (_, _, _) =>
        {
            if (Interlocked.Increment(ref running) > 1)
            {
                Interlocked.Increment(ref overlap);
            }

            Thread.Sleep(1);
            Interlocked.Decrement(ref running);
            return null;
        };

        await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => Task.Run(service.SyncNowAsync)));

        overlap.Should().Be(0);
    }

    private SyncService Create(SimDevice device)
    {
        FakeCatalogService catalog = new();
        var enrollments = TestEnrollments.Create(new FakeTrackingRepository(TimeProvider.System), catalog, _messenger);
        return new(
            device.Engine,
            device.Db,
            device.User,
            _entitlements,
            new DataChangeAnnouncer(enrollments, catalog, _messenger),
            _preferences,
            device.Clock,
            NullLogger<SyncService>.Instance,
            () => _available);
    }
}
