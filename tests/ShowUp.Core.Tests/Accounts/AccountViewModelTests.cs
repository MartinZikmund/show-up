using AwesomeAssertions;
using ShowUp.Core.Storage;
using ShowUp.Core.Supabase;
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
        _claim.HasProgress = true;
        await CodeSentAsync();
        _viewModel.Code = FakeSupabaseAuth.GoodCode;

        await _viewModel.VerifyCommand.ExecuteAsync(null);

        _confirmation.Asked.Should().Equal("AccountClaimTitle");
        _claim.ClaimedFor.Should().Equal(FakeSupabaseAuth.UserIdOf("alice@example.com"));
    }

    [TestMethod]
    public async Task SignOut_UnsyncedRows_Warns()
    {
        await SignedInAsync();
        _sync.HasUnsynced = true;
        _confirmation.Result = ConfirmationResult.Denied;

        await _viewModel.SignOutCommand.ExecuteAsync(null);

        _confirmation.Asked.Should().Equal("AccountSignOutUnsyncedTitle");
        _viewModel.IsSignedIn.Should().BeTrue("the user kept the account");
        _confirmation.Result = ConfirmationResult.Confirmed;
        await _viewModel.SignOutCommand.ExecuteAsync(null);
        _viewModel.IsSignedIn.Should().BeFalse();
        _viewModel.IsEnteringEmail.Should().BeTrue();
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
    }

    private sealed class FakeClaim : ILocalDataClaim
    {
        public bool HasProgress { get; set; }

        public List<string> ClaimedFor { get; } = [];

        public Task<bool> HasAnonymousProgressAsync() => Task.FromResult(HasProgress);

        public Task<int> ClaimAsync(string userId)
        {
            ClaimedFor.Add(userId);
            return Task.FromResult(1);
        }
    }
}
