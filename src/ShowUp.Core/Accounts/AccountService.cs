using Microsoft.Extensions.Logging;
using MZikmund.Toolkit.WinUI.Services;
using ShowUp.Core.Entitlements;
using ShowUp.Core.Messages;
using ShowUp.Core.Storage;
using ShowUp.Core.Supabase;

namespace ShowUp.Core.Accounts;

/// <summary>
/// Signing in and out, and what follows: the repositories' user scope, the store's user and one <see cref="DataChangedMessage"/>.
/// The first sign-in of an account on this device may claim the rows made while signed out; accounts never see each other's rows.
/// </summary>
public sealed class AccountService
{
    private const string ClaimAskedKey = "AccountClaimAsked";

    private readonly ISupabaseAuth _auth;
    private readonly UserScope _scope;
    private readonly IEntitlementService _entitlements;
    private readonly ILocalDataClaim _claim;
    private readonly IPreferences _preferences;
    private readonly DataChangeAnnouncer _announcer;
    private readonly ILogger<AccountService> _logger;
    private readonly Func<bool> _isAvailable;
    private readonly SynchronizationContext? _context = SynchronizationContext.Current;
    private readonly SemaphoreSlim _switchGate = new(1, 1);
    private int _interactive;

    public AccountService(
        ISupabaseAuth auth,
        UserScope scope,
        IEntitlementService entitlements,
        ILocalDataClaim claim,
        IPreferences preferences,
        DataChangeAnnouncer announcer,
        ILogger<AccountService> logger,
        Func<bool> isAvailable)
    {
        _auth = auth;
        _scope = scope;
        _entitlements = entitlements;
        _claim = claim;
        _preferences = preferences;
        _announcer = announcer;
        _logger = logger;
        _isAvailable = isAvailable;
        _auth.SessionChanged += OnSessionChanged;
    }

    /// <summary>False when this build has no account server (no Supabase project configured).</summary>
    public bool IsAvailable => _isAvailable();

    public Session? Current => _auth.Current;

    /// <summary>Signed in or out, on the thread that built this service when it had one.</summary>
    public event EventHandler? Changed;

    /// <summary>Picks the saved session back up before the first page loads. No network, no message: nothing has loaded yet.</summary>
    public async Task RestoreAsync()
    {
        await _auth.RestoreAsync();
        if (_auth.Current is { } session && _scope.Set(session.UserId))
        {
            await _entitlements.SetUserAsync(session.UserId);
        }
    }

    public Task SendCodeAsync(string email) => _auth.SendCodeAsync(email.Trim());

    /// <param name="confirmClaim">Asked on the account's first sign-in here when signed-out progress exists: true brings it into the account.</param>
    public async Task SignInAsync(string email, string code, Func<Task<bool>> confirmClaim)
    {
        Interlocked.Increment(ref _interactive);
        try
        {
            var session = await _auth.VerifyCodeAsync(email.Trim(), code);
            try
            {
                await OfferClaimAsync(session.UserId, confirmClaim);
            }
            catch
            {
                // All or nothing: signed out again, so the next attempt asks again.
                await _auth.SignOutAsync();
                throw;
            }

            await SwitchToAsync(session.UserId);
        }
        finally
        {
            Interlocked.Decrement(ref _interactive);
        }
    }

    /// <summary>Keeps every local row; the signed-out view comes back.</summary>
    public async Task SignOutAsync()
    {
        Interlocked.Increment(ref _interactive);
        try
        {
            await _auth.SignOutAsync();
            await SwitchToAsync(null);
        }
        finally
        {
            Interlocked.Decrement(ref _interactive);
        }
    }

    private async Task OfferClaimAsync(string userId, Func<Task<bool>> confirmClaim)
    {
        var key = ClaimAskedKey + AccountProCache.KeySuffix(userId);
        if (_preferences.ContainsKey(key) || !await _claim.HasAnonymousProgressAsync())
        {
            return;
        }

        if (await confirmClaim())
        {
            var claimed = await _claim.ClaimAsync(userId);
            _logger.LogInformation("Claimed {Count} signed-out rows for the account.", claimed);
        }

        // Asked once per account per device; a later "no" stays a "no".
        _preferences.Set(key, true);
    }

    private async Task SwitchToAsync(string? userId)
    {
        await _switchGate.WaitAsync();
        try
        {
            if (_scope.Set(userId))
            {
                await _entitlements.SetUserAsync(userId);
                _announcer.Announce();
            }
        }
        finally
        {
            _switchGate.Release();
        }

        Raise(() => Changed?.Invoke(this, EventArgs.Empty));
    }

    // Our own sign-in and sign-out switch themselves; this catches the rest, e.g. a refresh token the server revoked.
    private void OnSessionChanged(object? sender, Session? session)
    {
        if (Volatile.Read(ref _interactive) > 0)
        {
            return;
        }

        Raise(async () =>
        {
            try
            {
                await SwitchToAsync(session?.UserId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Switching the account after a session change failed.");
            }
        });
    }

    private void Raise(Action action)
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
}
