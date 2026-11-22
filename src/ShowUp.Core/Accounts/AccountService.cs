using Microsoft.Extensions.Logging;
using ShowUp.Core.Entitlements;
using ShowUp.Core.Messages;
using ShowUp.Core.Storage;
using ShowUp.Core.Supabase;

namespace ShowUp.Core.Accounts;

/// <summary>
/// Signing in and out, and what follows: the repositories' user scope, the store's user and one <see cref="DataChangedMessage"/>.
/// A sign-in on a device with signed-out progress offers to bring it into the account; accounts never see each other's rows.
/// </summary>
public sealed class AccountService
{
    /// <summary>A store that doesn't answer must not hold the switch (and the UI) hostage.</summary>
    public static readonly TimeSpan StoreUserTimeout = TimeSpan.FromSeconds(10);

    private readonly ISupabaseAuth _auth;
    private readonly UserScope _scope;
    private readonly IEntitlementService _entitlements;
    private readonly ILocalDataClaim _claim;
    private readonly SyncedPersonalization _personalization;
    private readonly DataChangeAnnouncer _announcer;
    private readonly TimeProvider _time;
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
        SyncedPersonalization personalization,
        DataChangeAnnouncer announcer,
        TimeProvider time,
        ILogger<AccountService> logger,
        Func<bool> isAvailable)
    {
        _auth = auth;
        _scope = scope;
        _entitlements = entitlements;
        _claim = claim;
        _personalization = personalization;
        _announcer = announcer;
        _time = time;
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
            await SetStoreUserAsync(session.UserId);
        }
    }

    public Task SendCodeAsync(string email) => _auth.SendCodeAsync(email.Trim());

    /// <param name="confirmClaim">Asked when this device has signed-out progress: true brings it into the account.</param>
    public async Task SignInAsync(string email, string code, Func<AnonymousProgress, Task<bool>> confirmClaim)
    {
        Interlocked.Increment(ref _interactive);
        try
        {
            var session = await _auth.VerifyCodeAsync(email.Trim(), code);
            try
            {
                await OfferClaimAsync(session.UserId, confirmClaim);
            }
            catch (Exception ex)
            {
                // All or nothing: signed out again, so the next attempt asks again. The claim's failure is the one to report.
                try
                {
                    await _auth.SignOutAsync();
                }
                catch (Exception signOut)
                {
                    _logger.LogWarning(signOut, "Undoing the sign-in after a failed claim failed too.");
                }

                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(ex);
            }

            // The session, not the one verified: a revocation meanwhile already ended it.
            await SwitchToAsync(_auth.Current?.UserId);
        }
        finally
        {
            await EndInteractiveAsync();
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
            await EndInteractiveAsync();
        }
    }

    private async Task OfferClaimAsync(string userId, Func<AnonymousProgress, Task<bool>> confirmClaim)
    {
        var progress = await _claim.GetAnonymousProgressAsync();
        if (progress.IsEmpty || !await confirmClaim(progress))
        {
            return;
        }

        var claimed = await _claim.ClaimAsync(userId);
        _logger.LogInformation("Claimed {Count} signed-out rows for the account.", claimed);
    }

    private async Task SwitchToAsync(string? userId)
    {
        await _switchGate.WaitAsync();
        try
        {
            if (_scope.CurrentUserId != userId)
            {
                // Pending writes belong to the account that made them.
                await _personalization.FlushAsync();
                _scope.Set(userId);
                // Together with the scope: a read or write meanwhile must not use the previous account's cached rows.
                _announcer.InvalidateCaches();
                try
                {
                    await SetStoreUserAsync(userId);
                }
                finally
                {
                    _announcer.Announce();
                }
            }
        }
        finally
        {
            _switchGate.Release();
        }

        Raise(() => Changed?.Invoke(this, EventArgs.Empty));
    }

    private async Task SetStoreUserAsync(string? userId)
    {
        try
        {
            await _entitlements.SetUserAsync(userId).WaitAsync(StoreUserTimeout, _time);
        }
        catch (TimeoutException)
        {
            _logger.LogWarning("The store didn't take the new user within {Timeout}; carrying on.", StoreUserTimeout);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "The store failed to take the new user; carrying on.");
        }
    }

    // A session change that arrived during our own sign-in or sign-out (e.g. a revocation) is applied once it's done.
    private async Task EndInteractiveAsync()
    {
        if (Interlocked.Decrement(ref _interactive) == 0 && _auth.Current?.UserId != _scope.CurrentUserId)
        {
            await SwitchToAsync(_auth.Current?.UserId);
        }
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
                await SwitchToAsync(_auth.Current?.UserId);
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
