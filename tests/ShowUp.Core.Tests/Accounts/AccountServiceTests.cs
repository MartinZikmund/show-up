using AwesomeAssertions;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging.Abstractions;
using ShowUp.Core.Accounts;
using ShowUp.Core.Marking;
using ShowUp.Core.Messages;
using ShowUp.Core.Storage;
using ShowUp.Core.Tests.Entitlements;
using ShowUp.Core.Tests.Storage;
using ShowUp.Core.Tests.Tracking;
using ShowUp.Core.Tracking;

namespace ShowUp.Core.Tests.Accounts;

[TestClass]
public sealed class AccountServiceTests : StorageTestBase
{
    private const string Alice = "alice@example.com";
    private const string Bob = "bob@example.com";

    private readonly FakeSupabaseAuth _auth = new();
    private readonly UserScope _scope = new();
    private readonly FakeEntitlementService _entitlements = new() { IsPro = true };
    private readonly WeakReferenceMessenger _messenger = new();
    private readonly FakeCatalogService _catalog = new(TestCatalog.Program("p", 1, "run"));
    private readonly List<AnonymousProgress> _offers = [];
    private readonly object _settle = new();
    private int _announced;
    private int _reloaded;
    private SqliteTrackingRepository _tracking = null!;
    private ActiveEnrollmentService _enrollments = null!;
    private SyncedPersonalization _personalization = null!;
    private AccountService _account = null!;

    [TestInitialize]
    public void Init()
    {
        _tracking = new(Db, Time, _scope, Device);
        _enrollments = TestEnrollments.Create(_tracking, _catalog, _messenger, entitlements: _entitlements);
        _personalization = new(Settings(), new MemoryPreferences(), _messenger, NullLogger<SyncedPersonalization>.Instance, _scope);
        _account = Create();
        _messenger.Register<DataChangedMessage>(_settle, (_, _) => Interlocked.Increment(ref _announced));
        _messenger.Register<PersonalizationReloadedMessage>(_settle, (_, _) => Interlocked.Increment(ref _reloaded));
    }

    // Each announcement reloads the personalization in the background; let it finish before the database file goes.
    [TestCleanup]
    public async Task SettleAsync() =>
        await WaitUntilAsync(() => Volatile.Read(ref _reloaded) >= Volatile.Read(ref _announced));

    private SqliteSyncedSettings Settings() => new(Db, Time, _scope, Device);

    private AccountService Create(ILocalDataClaim? claim = null) => new(
        _auth,
        _scope,
        _entitlements,
        claim ?? new SqliteLocalDataClaim(Db, Time, Device),
        _personalization,
        new DataChangeAnnouncer(_enrollments, _catalog, _messenger),
        Time,
        NullLogger<AccountService>.Instance,
        () => true);

    private Func<AnonymousProgress, Task<bool>> Answer(bool yes, Action? meanwhile = null) => progress =>
    {
        _offers.Add(progress);
        meanwhile?.Invoke();
        return Task.FromResult(yes);
    };

    [TestMethod]
    public async Task SignIn_FirstTime_AsksToClaim()
    {
        var enrollment = await _tracking.EnrollAsync("p", 1, "t", new DateOnly(2026, 12, 1));
        await _tracking.CrossSessionAsync(enrollment.Id, 0, 0, new DateOnly(2026, 12, 1));
        await _tracking.LogBonusAsync(null, "Walk", new DateOnly(2026, 12, 2), 20);

        await _account.SignInAsync(Alice, FakeSupabaseAuth.GoodCode, Answer(yes: true));

        _offers.Should().Equal(new AnonymousProgress(Programs: 1, CrossedWorkouts: 1, OtherEntries: 1));
    }

    [TestMethod]
    public async Task SignIn_NoSignedOutProgress_DoesNotAsk()
    {
        // Every install has settings rows (units are seeded on first start); they alone aren't worth asking about.
        await Settings().SetAsync("UnitSystem", "0");

        await _account.SignInAsync(Alice, FakeSupabaseAuth.GoodCode, Answer(yes: true));

        _offers.Should().BeEmpty();
        _scope.CurrentUserId.Should().Be(FakeSupabaseAuth.UserIdOf(Alice));
    }

    [TestMethod]
    public async Task Claim_Yes_SetsUserIdOnAnonymousRows()
    {
        var enrollment = await _tracking.EnrollAsync("p", 1, "t", new DateOnly(2026, 12, 1));
        await _tracking.CrossSessionAsync(enrollment.Id, 0, 0, new DateOnly(2026, 12, 1));
        await new SqliteReminderRepository(Db, Time, _scope, Device).AddAsync(420, 127);
        await Settings().SetAsync("MarkerColor", "2");
        var stampedAt = Time.GetUtcNow();
        Time.Advance(TimeSpan.FromMinutes(5));
        Device.DeviceId = "device-2";

        await _account.SignInAsync(Alice, FakeSupabaseAuth.GoodCode, Answer(yes: true));

        var userId = FakeSupabaseAuth.UserIdOf(Alice);
        (await _tracking.GetActiveEnrollmentsAsync()).Should().ContainSingle().Which.Id.Should().Be(enrollment.Id);
        (await _tracking.GetCompletedSessionsAsync(enrollment.Id)).Should().ContainSingle();
        (await new SqliteReminderRepository(Db, Time, _scope, Device).ListAsync()).Should().ContainSingle();
        (await Settings().GetAsync("MarkerColor")).Should().Be("2");
        var rows = await AllRowsAsync<EnrollmentEntity>();
        rows.Should().ContainSingle().Which.UserId.Should().Be(userId);
        rows[0].UpdatedAt.Should().BeAfter(stampedAt, "claimed rows are stamped so the next push sends them");
        rows[0].DeviceId.Should().Be("device-2");
        (await AllRowsAsync<CompletionEntity>()).Should().OnlyContain(r => r.UserId == userId);
        (await AllRowsAsync<SyncedSettingEntity>()).Should().OnlyContain(r => r.UserId == userId);
    }

    [TestMethod]
    public async Task Claim_No_KeepsRowsLocalAndHiddenWhileSignedIn()
    {
        await _tracking.EnrollAsync("p", 1, "t", new DateOnly(2026, 12, 1));

        await _account.SignInAsync(Alice, FakeSupabaseAuth.GoodCode, Answer(yes: false));

        (await _tracking.GetActiveEnrollmentsAsync()).Should().BeEmpty();
        (await AllRowsAsync<EnrollmentEntity>()).Should().ContainSingle().Which.UserId.Should().BeNull();
        await _account.SignOutAsync();
        (await _tracking.GetActiveEnrollmentsAsync()).Should().ContainSingle("signing out shows the signed-out rows again");
    }

    [TestMethod]
    public async Task SignIn_AgainWithSignedOutRows_OffersThemAgain()
    {
        await _tracking.EnrollAsync("p", 1, "t", new DateOnly(2026, 12, 1));
        await _account.SignInAsync(Alice, FakeSupabaseAuth.GoodCode, Answer(yes: true));
        await _account.SignOutAsync();
        await _tracking.LogBonusAsync(null, "Walk", new DateOnly(2026, 12, 2), 20);

        await _account.SignInAsync(Alice, FakeSupabaseAuth.GoodCode, Answer(yes: true));

        _offers.Should().HaveCount(2);
        _offers[1].Should().Be(new AnonymousProgress(0, 0, 1), "only what was made since signing out");
        (await AllRowsAsync<BonusWorkoutEntity>()).Should().OnlyContain(b => b.UserId == FakeSupabaseAuth.UserIdOf(Alice));
    }

    [TestMethod]
    public async Task SignIn_AgainWithNothingSignedOut_DoesNotAsk()
    {
        await _tracking.EnrollAsync("p", 1, "t", new DateOnly(2026, 12, 1));
        await _account.SignInAsync(Alice, FakeSupabaseAuth.GoodCode, Answer(yes: true));
        await _account.SignOutAsync();

        await _account.SignInAsync(Alice, FakeSupabaseAuth.GoodCode, Answer(yes: true));

        _offers.Should().ContainSingle();
    }

    [TestMethod]
    public async Task SignIn_DifferentAccount_DoesNotUploadOthersRows()
    {
        var alices = await _tracking.EnrollAsync("p", 1, "t", new DateOnly(2026, 12, 1));
        await _account.SignInAsync(Alice, FakeSupabaseAuth.GoodCode, Answer(yes: true));
        await _account.SignOutAsync();

        await _account.SignInAsync(Bob, FakeSupabaseAuth.GoodCode, Answer(yes: true));

        _offers.Should().ContainSingle("Alice's rows are hers, not signed-out progress Bob could claim");
        (await _tracking.GetActiveEnrollmentsAsync()).Should().BeEmpty();
        (await AllRowsAsync<EnrollmentEntity>()).Should().ContainSingle().Which.UserId.Should().Be(FakeSupabaseAuth.UserIdOf(Alice));
        await _account.SignOutAsync();
        await _account.SignInAsync(Alice, FakeSupabaseAuth.GoodCode, Answer(yes: true));
        (await _tracking.GetActiveEnrollmentsAsync()).Should().ContainSingle().Which.Id.Should().Be(alices.Id);
    }

    [TestMethod]
    public async Task SignIn_SwitchesScopeThenStoreUserThenAnnouncesOnce()
    {
        await _enrollments.GetAllAsync();
        await _tracking.EnrollAsync("p", 1, "t", new DateOnly(2026, 12, 1));
        List<string> seen = [];
        _messenger.Register<DataChangedMessage>(this, (_, _) => seen.Add($"scope={_scope.CurrentUserId}, store={_entitlements.Users.LastOrDefault()}"));

        await _account.SignInAsync(Alice, FakeSupabaseAuth.GoodCode, Answer(yes: true));

        var userId = FakeSupabaseAuth.UserIdOf(Alice);
        seen.Should().Equal($"scope={userId}, store={userId}");
        _entitlements.Users.Should().Equal(userId);
        (await _enrollments.GetAllAsync()).Should().ContainSingle("the cached empty list was dropped before the message");
    }

    [TestMethod]
    public async Task Cross_WhileTheStoreTakesTheNewUser_UsesTheNewScope()
    {
        var signedOut = await _tracking.EnrollAsync("p", 1, "t", new DateOnly(2026, 12, 1));
        (await _enrollments.GetAllAsync()).Should().ContainSingle();
        TaskCompletionSource store = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _entitlements.SetUserGate = store.Task;

        var signIn = _account.SignInAsync(Alice, FakeSupabaseAuth.GoodCode, Answer(yes: false));
        await WaitUntilAsync(() => _entitlements.Users.Count == 1);

        (await _enrollments.GetAllAsync()).Should().BeEmpty("the cache dropped together with the scope, not after the store answered");
        var cross = () => _enrollments.CrossAsync(signedOut.Id, 0, 0);
        await cross.Should().ThrowAsync<InvalidOperationException>("the signed-out program isn't Alice's");
        store.SetResult();
        await signIn;
        (await _tracking.GetCompletedSessionsAsync(signedOut.Id)).Should().BeEmpty();
    }

    [TestMethod]
    public async Task SignIn_StoreNeverTakesTheUser_CarriesOnAfterTheTimeout()
    {
        _entitlements.SetUserGate = new TaskCompletionSource().Task;
        var changes = 0;
        _messenger.Register<DataChangedMessage>(this, (_, _) => changes++);

        var signIn = _account.SignInAsync(Alice, FakeSupabaseAuth.GoodCode, Answer(yes: true));
        await WaitUntilAsync(() => _entitlements.Users.Count == 1);
        Time.Advance(AccountService.StoreUserTimeout + TimeSpan.FromSeconds(1));
        await signIn.WaitAsync(TimeSpan.FromSeconds(5));

        changes.Should().Be(1);
        _scope.CurrentUserId.Should().Be(FakeSupabaseAuth.UserIdOf(Alice));
    }

    [TestMethod]
    public async Task SignOut_ReturnsToAnonymousAndKeepsEveryRow()
    {
        await _tracking.EnrollAsync("p", 1, "t", new DateOnly(2026, 12, 1));
        await _account.SignInAsync(Alice, FakeSupabaseAuth.GoodCode, Answer(yes: true));
        var changes = 0;
        _messenger.Register<DataChangedMessage>(this, (_, _) => changes++);

        await _account.SignOutAsync();

        _scope.CurrentUserId.Should().BeNull();
        _entitlements.Users.Should().EndWith((string?)null);
        changes.Should().Be(1);
        (await AllRowsAsync<EnrollmentEntity>()).Should().ContainSingle();
        _account.Current.Should().BeNull();
    }

    [TestMethod]
    public async Task SignOut_PendingPersonalizationWrite_LandsInTheAccountThatMadeIt()
    {
        await _account.SignInAsync(Alice, FakeSupabaseAuth.GoodCode, Answer(yes: true));
        _personalization.MarkerColor = MarkerColor.Blue;

        await _account.SignOutAsync();

        (await AllRowsAsync<SyncedSettingEntity>()).Should().ContainSingle(s => s.Key == "MarkerColor")
            .Which.UserId.Should().Be(FakeSupabaseAuth.UserIdOf(Alice));
    }

    [TestMethod]
    public async Task SessionRevokedByTheServer_SwitchesToAnonymous()
    {
        await _account.SignInAsync(Alice, FakeSupabaseAuth.GoodCode, Answer(yes: true));
        var changed = 0;
        _account.Changed += (_, _) => changed++;

        _auth.ForceSignOut();

        _scope.CurrentUserId.Should().BeNull();
        _entitlements.Users.Should().EndWith((string?)null);
        changed.Should().Be(1);
    }

    [TestMethod]
    public async Task SessionRevokedDuringSignIn_IsAppliedAfterwards()
    {
        await _tracking.EnrollAsync("p", 1, "t", new DateOnly(2026, 12, 1));

        await _account.SignInAsync(Alice, FakeSupabaseAuth.GoodCode, Answer(yes: false, meanwhile: _auth.ForceSignOut));

        _account.Current.Should().BeNull();
        _scope.CurrentUserId.Should().BeNull("the revocation arrived mid sign-in and must not be lost");
    }

    [TestMethod]
    public async Task Restore_SavedSession_SetsTheScopeWithoutAnnouncing()
    {
        _auth.Saved = new("access", "refresh", DateTimeOffset.MaxValue, "user-1", Alice);
        var changes = 0;
        _messenger.Register<DataChangedMessage>(this, (_, _) => changes++);

        await _account.RestoreAsync();

        _scope.CurrentUserId.Should().Be("user-1");
        _entitlements.Users.Should().Equal("user-1");
        changes.Should().Be(0, "nothing has loaded yet");
    }

    [TestMethod]
    public async Task SignIn_ClaimFails_SignsOutAgainAndAsksNextTime()
    {
        await _tracking.EnrollAsync("p", 1, "t", new DateOnly(2026, 12, 1));
        var account = Create(new ThrowingClaim());

        var act = () => account.SignInAsync(Alice, FakeSupabaseAuth.GoodCode, Answer(yes: true));

        await act.Should().ThrowAsync<InvalidOperationException>();
        _auth.Current.Should().BeNull();
        _scope.CurrentUserId.Should().BeNull();
        await _account.SignInAsync(Alice, FakeSupabaseAuth.GoodCode, Answer(yes: true));
        _offers.Should().HaveCount(2);
    }

    [TestMethod]
    public async Task SignIn_ClaimAndUndoBothFail_ReportsTheClaimFailure()
    {
        _auth.SignOutError = new HttpRequestException("offline");
        var account = Create(new ThrowingClaim());
        TaskCompletionSource reloaded = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _messenger.Register<PersonalizationReloadedMessage>(this, (_, _) => reloaded.TrySetResult());

        var act = () => account.SignInAsync(Alice, FakeSupabaseAuth.GoodCode, Answer(yes: true));

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("database is locked");
        _scope.CurrentUserId.Should().Be(FakeSupabaseAuth.UserIdOf(Alice), "the session survived, so the scope follows it");
        await reloaded.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(5));
        while (!condition())
        {
            await Task.Delay(5, timeout.Token);
        }
    }

    private async Task<List<T>> AllRowsAsync<T>()
        where T : SyncableEntity, new() => await (await Db.GetConnectionAsync()).Table<T>().ToListAsync();

    private sealed class ThrowingClaim : ILocalDataClaim
    {
        public Task<AnonymousProgress> GetAnonymousProgressAsync() => Task.FromResult(new AnonymousProgress(1, 0, 0));

        public Task<int> ClaimAsync(string userId) => throw new InvalidOperationException("database is locked");
    }
}
