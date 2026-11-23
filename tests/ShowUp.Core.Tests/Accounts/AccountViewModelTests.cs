using AwesomeAssertions;
using Microsoft.Extensions.Time.Testing;
using ShowUp.Core.Storage;
using ShowUp.Core.Supabase;
using ShowUp.Core.Sync;
using ShowUp.Core.ViewModels;
using ShowUp.Services.Dialogs;

namespace ShowUp.Core.Tests.Accounts;

[TestClass]
public sealed class AccountViewModelTests
{
    private readonly FakeSupabaseAuth _auth = new();
    private readonly FakeSyncStatus _sync = new();
    private readonly RecordingConfirmation _confirmation = new();
    private readonly FakeClaim _claim = new();
    private readonly AccountViewModel _viewModel;

    public AccountViewModelTests()
    {
        _viewModel = AccountTestFactory.CreateViewModel(AccountTestFactory.CreateService(_auth, _claim), _sync, _confirmation);
        _viewModel.Activate();
    }

    [TestMethod]
    public async Task SessionRevoked_ShowsTheSessionEndedNoteUntilDismissed()
    {
        _viewModel.Email = "alice@example.com";
        await _viewModel.SendCodeCommand.ExecuteAsync(null);
        _viewModel.Code = FakeSupabaseAuth.GoodCode;
        await _viewModel.VerifyCommand.ExecuteAsync(null);
        _viewModel.HasSessionEnded.Should().BeFalse();

        _auth.ForceSignOut();
        _viewModel.Update();
        _viewModel.HasSessionEnded.Should().BeTrue();

        _viewModel.DismissSessionEndedCommand.Execute(null);

        _viewModel.HasSessionEnded.Should().BeFalse();
    }

    [TestMethod]
    public async Task SendCode_ValidEmail_AsksForTheCode()
    {
        _viewModel.Email = " alice@example.com ";

        await _viewModel.SendCodeCommand.ExecuteAsync(null);

        _auth.CodesSentTo.Should().Equal("alice@example.com");
        _viewModel.IsEnteringCode.Should().BeTrue();
        _viewModel.IsEnteringEmail.Should().BeFalse();
        _viewModel.CodeSentText.Should().Be("AccountCodeSentFormat(alice@example.com)");
    }

    [TestMethod]
    public async Task SendCode_NotAnEmail_ShowsAnErrorWithoutCalling()
    {
        _viewModel.Email = "alice";

        await _viewModel.SendCodeCommand.ExecuteAsync(null);

        _auth.CodesSentTo.Should().BeEmpty();
        _viewModel.ErrorText.Should().Be("AccountErrorInvalidEmail");
        _viewModel.IsEnteringEmail.Should().BeTrue();
    }

    [TestMethod]
    [DataRow(SupabaseErrorKind.RateLimited, "AccountErrorRateLimited")]
    [DataRow(SupabaseErrorKind.Network, "AccountErrorNetwork")]
    [DataRow(SupabaseErrorKind.Server, "AccountErrorNetwork")]
    [DataRow(SupabaseErrorKind.Validation, "AccountErrorInvalidEmail")]
    [DataRow(SupabaseErrorKind.Unauthorized, "AccountErrorGeneric")]
    public async Task SendCode_ServerError_ShowsItLocalized(SupabaseErrorKind kind, string expected)
    {
        _auth.SendError = new(kind, "nope");
        _viewModel.Email = "alice@example.com";

        await _viewModel.SendCodeCommand.ExecuteAsync(null);

        _viewModel.ErrorText.Should().Be(expected);
        _viewModel.IsBusy.Should().BeFalse();
        _viewModel.IsCodeSent.Should().BeFalse();
    }

    [TestMethod]
    public async Task Verify_WrongCode_ShowsInvalidCode()
    {
        await CodeSentAsync();
        _viewModel.Code = "654321";

        await _viewModel.VerifyCommand.ExecuteAsync(null);

        _viewModel.ErrorText.Should().Be("AccountErrorInvalidCode");
        _viewModel.IsSignedIn.Should().BeFalse();
        _viewModel.IsEnteringCode.Should().BeTrue();
    }

    [TestMethod]
    public async Task Verify_TooShort_ShowsTheFormatError()
    {
        await CodeSentAsync();
        _viewModel.Code = "1234";

        await _viewModel.VerifyCommand.ExecuteAsync(null);

        _viewModel.ErrorText.Should().Be("AccountErrorCodeFormat");
    }

    [TestMethod]
    public async Task Verify_PastedWithSpaces_SignsIn()
    {
        await CodeSentAsync();
        _viewModel.Code = " 123 456 ";

        await _viewModel.VerifyCommand.ExecuteAsync(null);

        _viewModel.IsSignedIn.Should().BeTrue();
        _viewModel.SignedInEmail.Should().Be("alice@example.com");
        _viewModel.IsEnteringCode.Should().BeFalse();
        _viewModel.Code.Should().BeEmpty();
    }

    [TestMethod]
    public async Task SignIn_FirstTime_AsksToClaimInADialog()
    {
        _claim.Progress = new(Programs: 1, CrossedWorkouts: 23, OtherEntries: 0);
        await CodeSentAsync();
        _viewModel.Code = FakeSupabaseAuth.GoodCode;

        await _viewModel.VerifyCommand.ExecuteAsync(null);

        _confirmation.Asked.Should().Equal("AccountClaimTitleFormat(AccountClaimJoinTwo(AccountClaimPrograms_One(1),AccountClaimCrossed_Other(23)))");
        _claim.ClaimedFor.Should().Equal(FakeSupabaseAuth.UserIdOf("alice@example.com"));
    }

    [TestMethod]
    public async Task SignOut_UnsyncedRows_WarnsAndCancelKeepsTheAccount()
    {
        await SignedInAsync();
        _sync.HasUnsynced = true;
        _confirmation.Choice = DialogChoice.Cancel;

        await _viewModel.SignOutCommand.ExecuteAsync(null);

        _confirmation.Choices.Should().Equal("AccountSignOutUnsyncedTitle");
        _viewModel.IsSignedIn.Should().BeTrue("the user kept the account");
        _sync.SyncNowCalls.Should().Be(0);
    }

    [TestMethod]
    public async Task SignOut_UnsyncedRowsSignOutAnyway_SignsOut()
    {
        await SignedInAsync();
        _sync.HasUnsynced = true;
        _confirmation.Choice = DialogChoice.Secondary;

        await _viewModel.SignOutCommand.ExecuteAsync(null);

        _viewModel.IsSignedIn.Should().BeFalse();
        _viewModel.IsEnteringEmail.Should().BeTrue();
        _sync.SyncNowCalls.Should().Be(0);
    }

    [TestMethod]
    public async Task SignOut_UnsyncedRowsSyncNow_SyncsThenSignsOut()
    {
        await SignedInAsync();
        _sync.HasUnsynced = true;
        _confirmation.Choice = DialogChoice.Primary;

        await _viewModel.SignOutCommand.ExecuteAsync(null);

        _sync.SyncNowCalls.Should().Be(1);
        _viewModel.IsSignedIn.Should().BeFalse();
    }

    [TestMethod]
    public async Task SignOut_SyncNowLeavesRowsBehind_StaysSignedInAndSaysSo()
    {
        await SignedInAsync();
        _sync.HasUnsynced = true;
        _sync.SyncNowSucceeds = false;
        _confirmation.Choice = DialogChoice.Primary;

        await _viewModel.SignOutCommand.ExecuteAsync(null);

        _viewModel.IsSignedIn.Should().BeTrue();
        _viewModel.ErrorText.Should().Be("AccountSignOutStillUnsynced");
    }

    [TestMethod]
    [DataRow(SyncState.Syncing, "SyncStatusSyncing")]
    [DataRow(SyncState.Offline, "SyncStatusOffline")]
    [DataRow(SyncState.SignInAgain, "SyncStatusSignInAgain")]
    [DataRow(SyncState.Paused, "SyncStatusPaused")]
    [DataRow(SyncState.Failed, "SyncStatusFailed")]
    [DataRow(SyncState.Idle, "SyncStatusNever")]
    public async Task SyncStatus_State_ShowsIt(SyncState state, string expected)
    {
        await SignedInAsync();

        _sync.Raise(state);

        _viewModel.SyncStatusText.Should().Be(expected);
    }

    [TestMethod]
    [DataRow(20, "SyncLastSyncedFormat(SyncJustNow)")]
    [DataRow(5 * 60, "SyncLastSyncedFormat(SyncMinutesAgo_Other(5))")]
    [DataRow(3 * 3600, "SyncLastSyncedFormat(SyncHoursAgo_Other(3))")]
    [DataRow(2 * 86400, "SyncLastSyncedFormat(SyncDaysAgo_Other(2))")]
    public async Task SyncStatus_Idle_ShowsWhenItLastSynced(int secondsAgo, string expected)
    {
        FakeTimeProvider time = new(new DateTimeOffset(2026, 12, 10, 12, 0, 0, TimeSpan.Zero));
        var viewModel = AccountTestFactory.CreateViewModel(AccountTestFactory.CreateService(_auth, _claim), _sync, _confirmation, time);
        viewModel.Activate();
        viewModel.Email = "alice@example.com";
        await viewModel.SendCodeCommand.ExecuteAsync(null);
        viewModel.Code = FakeSupabaseAuth.GoodCode;
        await viewModel.VerifyCommand.ExecuteAsync(null);

        _sync.Raise(SyncState.Idle, time.GetUtcNow().AddSeconds(-secondsAgo));

        viewModel.SyncStatusText.Should().Be(expected);
    }

    [TestMethod]
    public async Task SyncNow_RunsASync()
    {
        await SignedInAsync();

        await _viewModel.SyncNowCommand.ExecuteAsync(null);

        _sync.SyncNowCalls.Should().Be(1);
    }

    [TestMethod]
    [DataRow(SyncState.Paused)]
    [DataRow(SyncState.Syncing)]
    public async Task SyncNow_PausedOrRunning_IsDisabled(SyncState state)
    {
        await SignedInAsync();

        _sync.Raise(state);

        _viewModel.SyncNowCommand.CanExecute(null).Should().BeFalse();
        _viewModel.IsSyncing.Should().Be(state == SyncState.Syncing);
    }

    [TestMethod]
    public async Task SignOut_EverythingSynced_SignsOutWithoutAsking()
    {
        await SignedInAsync();

        await _viewModel.SignOutCommand.ExecuteAsync(null);

        _confirmation.Asked.Should().BeEmpty();
        _viewModel.IsSignedIn.Should().BeFalse();
    }

    [TestMethod]
    public async Task SessionEndedElsewhere_ShowsSignedOut()
    {
        await SignedInAsync();

        _auth.ForceSignOut();

        _viewModel.IsSignedIn.Should().BeFalse();
    }

    [TestMethod]
    public async Task ChangeEmail_BackToTheEmailBox()
    {
        await CodeSentAsync();

        _viewModel.ChangeEmailCommand.Execute(null);

        _viewModel.IsEnteringEmail.Should().BeTrue();
        _viewModel.Email.Should().Be("alice@example.com", "a typo is fixed, not retyped");
    }

    [TestMethod]
    [DataRow(1, 0, 0, "AccountClaimPrograms_One(1)")]
    [DataRow(0, 0, 7, "AccountClaimEntries_Other(7)")]
    [DataRow(0, 23, 7, "AccountClaimJoinTwo(AccountClaimCrossed_Other(23),AccountClaimOtherEntries_Other(7))")]
    [DataRow(1, 1, 1, "AccountClaimJoinThree(AccountClaimPrograms_One(1),AccountClaimCrossed_One(1),AccountClaimOtherEntries_One(1))")]
    public void DescribeProgress_LeavesOutWhatThereIsNoneOf(int programs, int crossed, int other, string expected) =>
        _viewModel.DescribeProgress(new(programs, crossed, other)).Should().Be(expected);

    [TestMethod]
    public async Task ResendCode_RightAfterSending_WaitsForTheCooldown()
    {
        FakeTimeProvider time = new();
        var viewModel = AccountTestFactory.CreateViewModel(AccountTestFactory.CreateService(_auth), time: time);
        viewModel.Email = "alice@example.com";
        await viewModel.SendCodeCommand.ExecuteAsync(null);

        viewModel.ResendCodeCommand.CanExecute(null).Should().BeFalse();
        var raised = 0;
        viewModel.ResendCodeCommand.CanExecuteChanged += (_, _) => raised++;
        time.Advance(AccountViewModel.ResendCooldown);

        raised.Should().BeGreaterThan(0, "the button comes back by itself");
        viewModel.ResendCodeCommand.CanExecute(null).Should().BeTrue();
        await viewModel.ResendCodeCommand.ExecuteAsync(null);
        _auth.CodesSentTo.Should().HaveCount(2);
        viewModel.ResendCodeCommand.CanExecute(null).Should().BeFalse("the cooldown starts again");
    }

    [TestMethod]
    [DataRow("123456", "123456")]
    [DataRow("123-456", "123456")]
    [DataRow("Your code: 123456", "123456")]
    [DataRow("12345", null)]
    [DataRow("1234567", null)]
    [DataRow(null, null)]
    public void NormalizeCode_Pasted_KeepsSixDigits(string? input, string? expected) =>
        AccountViewModel.NormalizeCode(input).Should().Be(expected);

    private async Task CodeSentAsync()
    {
        _viewModel.Email = "alice@example.com";
        await _viewModel.SendCodeCommand.ExecuteAsync(null);
    }

    private async Task SignedInAsync()
    {
        await CodeSentAsync();
        _viewModel.Code = FakeSupabaseAuth.GoodCode;
        await _viewModel.VerifyCommand.ExecuteAsync(null);
    }

    private sealed class RecordingConfirmation : IConfirmationDialogService
    {
        public List<string> Asked { get; } = [];

        public ConfirmationResult Result { get; set; } = ConfirmationResult.Confirmed;

        public Task<ConfirmationResult> ShowAsync(string title, string text)
        {
            Asked.Add(title);
            return Task.FromResult(Result);
        }

        public DialogChoice Choice { get; set; } = DialogChoice.Cancel;

        public List<string> Choices { get; } = [];

        public Task<DialogChoice> ChooseAsync(string title, string text, string primary, string secondary)
        {
            Choices.Add(title);
            return Task.FromResult(Choice);
        }
    }

    private sealed class FakeClaim : ILocalDataClaim
    {
        public AnonymousProgress Progress { get; set; } = new(0, 0, 0);

        public List<string> ClaimedFor { get; } = [];

        public Task<AnonymousProgress> GetAnonymousProgressAsync() => Task.FromResult(Progress);

        public Task<int> ClaimAsync(string userId)
        {
            ClaimedFor.Add(userId);
            return Task.FromResult(1);
        }
    }
}
