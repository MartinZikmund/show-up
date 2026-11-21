using Microsoft.Extensions.Logging;
using ShowUp.Core.Accounts;
using ShowUp.Core.Localization;
using ShowUp.Core.Storage;
using ShowUp.Core.Supabase;
using ShowUp.Core.Sync;
using ShowUp.Services.Dialogs;

namespace ShowUp.Core.ViewModels;

/// <summary>The Settings account card: email, then the emailed 6-digit code, then who is signed in and "Sign out".</summary>
public partial class AccountViewModel : ObservableObject
{
    public const int CodeLength = 6;

    /// <summary>Between two codes to the same inbox; Supabase rate-limits faster resends anyway.</summary>
    public static readonly TimeSpan ResendCooldown = TimeSpan.FromSeconds(30);

    private readonly AccountService _account;
    private readonly ISyncStatus _sync;
    private readonly IConfirmationDialogService _confirmation;
    private readonly IStringLocalizer _localizer;
    private readonly TimeProvider _time;
    private readonly ILogger<AccountViewModel> _logger;
    private readonly SynchronizationContext? _context = SynchronizationContext.Current;
    private bool _isActive;
    private DateTimeOffset _resendAt;
    private ITimer? _resendTimer;

    public AccountViewModel(
        AccountService account,
        ISyncStatus sync,
        IConfirmationDialogService confirmation,
        IStringLocalizer localizer,
        TimeProvider time,
        ILogger<AccountViewModel> logger)
    {
        _account = account;
        _sync = sync;
        _confirmation = confirmation;
        _localizer = localizer;
        _time = time;
        _logger = logger;
        Update();
    }

    [ObservableProperty]
    public partial bool IsAvailable { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSignedOut), nameof(IsEnteringEmail), nameof(IsEnteringCode))]
    public partial bool IsSignedIn { get; private set; }

    public bool IsSignedOut => !IsSignedIn;

    [ObservableProperty]
    public partial string? SignedInEmail { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCodeCommand))]
    public partial string Email { get; set; } = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(VerifyCommand))]
    public partial string Code { get; set; } = "";

    /// <summary>The code went out; the card asks for it.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEnteringEmail), nameof(IsEnteringCode), nameof(CodeSentText))]
    public partial bool IsCodeSent { get; private set; }

    public bool IsEnteringEmail => !IsSignedIn && !IsCodeSent;

    public bool IsEnteringCode => !IsSignedIn && IsCodeSent;

    public string CodeSentText => IsCodeSent ? _localizer["AccountCodeSentFormat", Email.Trim()] : "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCodeCommand), nameof(ResendCodeCommand), nameof(VerifyCommand), nameof(SignOutCommand), nameof(ChangeEmailCommand))]
    public partial bool IsBusy { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string? ErrorText { get; private set; }

    public bool HasError => !string.IsNullOrEmpty(ErrorText);

    public void Activate()
    {
        if (!_isActive)
        {
            _account.Changed += OnAccountChanged;
            _isActive = true;
        }

        Update();
    }

    public void Deactivate()
    {
        if (_isActive)
        {
            _account.Changed -= OnAccountChanged;
            _isActive = false;
        }
    }

    /// <summary>Re-reads whether the card shows at all (a debug build can switch the fake account server on).</summary>
    public void Update()
    {
        IsAvailable = _account.IsAvailable;
        var session = _account.Current;
        IsSignedIn = session is not null;
        SignedInEmail = session?.Email;
        if (IsSignedIn)
        {
            IsCodeSent = false;
            Code = "";
        }
    }

    /// <summary>A pasted "123 456" or "Your code: 123456" still counts.</summary>
    public static string? NormalizeCode(string? input)
    {
        var digits = new string((input ?? "").Where(char.IsAsciiDigit).ToArray());
        return digits.Length == CodeLength ? digits : null;
    }

    public static bool LooksLikeEmail(string? input) =>
        input?.Trim() is { Length: >= 3 } email
        && email.IndexOf('@') is > 0 and var at
        && at < email.Length - 1
        && !email.Any(char.IsWhiteSpace);

    private bool CanSendCode() => !IsBusy && !string.IsNullOrWhiteSpace(Email);

    [RelayCommand(CanExecute = nameof(CanSendCode))]
    private async Task SendCodeAsync()
    {
        if (!LooksLikeEmail(Email))
        {
            ErrorText = _localizer["AccountErrorInvalidEmail"];
            return;
        }

        await RunAsync(async () =>
        {
            await _account.SendCodeAsync(Email);
            Code = "";
            IsCodeSent = true;
            StartResendCooldown();
        });
    }

    private bool CanResendCode() => !IsBusy && _time.GetUtcNow() >= _resendAt;

    [RelayCommand(CanExecute = nameof(CanResendCode))]
    private Task ResendCodeAsync() => SendCodeAsync();

    private void StartResendCooldown()
    {
        _resendAt = _time.GetUtcNow() + ResendCooldown;
        _resendTimer?.Dispose();
        _resendTimer = _time.CreateTimer(_ => Post(ResendCodeCommand.NotifyCanExecuteChanged), null, ResendCooldown, Timeout.InfiniteTimeSpan);
        ResendCodeCommand.NotifyCanExecuteChanged();
    }

    private void Post(Action action)
    {
        if (_context is null || _context == SynchronizationContext.Current)
        {
            action();
        }
        else
        {
            _context.Post(_ => action(), null);
        }
    }

    private bool CanVerify() => !IsBusy && !string.IsNullOrWhiteSpace(Code);

    [RelayCommand(CanExecute = nameof(CanVerify))]
    private async Task VerifyAsync()
    {
        if (NormalizeCode(Code) is not { } code)
        {
            ErrorText = _localizer["AccountErrorCodeFormat"];
            return;
        }

        await RunAsync(() => _account.SignInAsync(Email, code, ConfirmClaimAsync));
        Update();
    }

    private bool CanChangeEmail() => !IsBusy;

    /// <summary>Back to the email box, e.g. after a typo.</summary>
    [RelayCommand(CanExecute = nameof(CanChangeEmail))]
    private void ChangeEmail()
    {
        IsCodeSent = false;
        Code = "";
        ErrorText = null;
    }

    private bool CanSignOut() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanSignOut))]
    private async Task SignOutAsync()
    {
        await RunAsync(async () =>
        {
            if (await _sync.HasUnsyncedChangesAsync()
                && await _confirmation.ShowAsync(_localizer["AccountSignOutUnsyncedTitle"], _localizer["AccountSignOutUnsyncedMessage"]) != ConfirmationResult.Confirmed)
            {
                return;
            }

            await _account.SignOutAsync();
            Email = "";
        });
        Update();
    }

    private async Task<bool> ConfirmClaimAsync(AnonymousProgress progress) =>
        await _confirmation.ShowAsync(_localizer["AccountClaimTitleFormat", DescribeProgress(progress)], _localizer["AccountClaimMessage"]) == ConfirmationResult.Confirmed;

    /// <summary>"2 programs, 23 crossed workouts and 4 other entries", leaving out what there's none of.</summary>
    public string DescribeProgress(AnonymousProgress progress)
    {
        List<string> parts = [];
        if (progress.Programs > 0)
        {
            parts.Add(_localizer.Format("AccountClaimPrograms", progress.Programs));
        }

        if (progress.CrossedWorkouts > 0)
        {
            parts.Add(_localizer.Format("AccountClaimCrossed", progress.CrossedWorkouts));
        }

        if (progress.OtherEntries > 0)
        {
            parts.Add(_localizer.Format(parts.Count == 0 ? "AccountClaimEntries" : "AccountClaimOtherEntries", progress.OtherEntries));
        }

        return parts.Count switch
        {
            1 => parts[0],
            2 => _localizer["AccountClaimJoinTwo", parts[0], parts[1]],
            _ => _localizer["AccountClaimJoinThree", parts[0], parts[1], parts[2]],
        };
    }

    private async Task RunAsync(Func<Task> work)
    {
        IsBusy = true;
        ErrorText = null;
        try
        {
            await work();
        }
        catch (SupabaseException ex)
        {
            _logger.LogWarning("An account call failed ({Kind}, {Status}).", ex.Kind, ex.StatusCode);
            ErrorText = _localizer[ex.Kind switch
            {
                SupabaseErrorKind.InvalidCode => "AccountErrorInvalidCode",
                SupabaseErrorKind.Validation or SupabaseErrorKind.Forbidden when IsCodeSent => "AccountErrorInvalidCode",
                SupabaseErrorKind.RateLimited => "AccountErrorRateLimited",
                SupabaseErrorKind.Network or SupabaseErrorKind.Server => "AccountErrorNetwork",
                SupabaseErrorKind.Validation when !IsCodeSent => "AccountErrorInvalidEmail",
                _ => "AccountErrorGeneric",
            }];
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An account call failed.");
            ErrorText = _localizer["AccountErrorGeneric"];
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void OnAccountChanged(object? sender, EventArgs e) => Update();
}
