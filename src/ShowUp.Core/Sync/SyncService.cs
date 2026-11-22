using Microsoft.Extensions.Logging;
using MZikmund.Toolkit.WinUI.Services;
using ShowUp.Core.Entitlements;
using ShowUp.Core.Messages;
using ShowUp.Core.Storage;
using ShowUp.Core.Supabase;

namespace ShowUp.Core.Sync;

public enum SyncState
{
    /// <summary>No account server in this build.</summary>
    Off,
    SignedOut,

    /// <summary>Signed in without Pro: nothing syncs, nothing is deleted.</summary>
    Paused,
    Idle,
    Syncing,

    /// <summary>The server couldn't be reached; a retry is scheduled.</summary>
    Offline,

    /// <summary>The server rejected the session even after a refresh; unsent rows wait for the next sign-in.</summary>
    SignInAgain,

    /// <summary>Something else went wrong; a retry is scheduled.</summary>
    Failed,
}

/// <summary>
/// When to sync (app start, resume, 5 s after a local change, "Sync now", sign-in, regaining Pro), one sync at a time,
/// backing off while offline, and what the account card shows. See docs/sync.md.
/// </summary>
public sealed class SyncService : ISyncStatus, IDisposable
{
    public static readonly TimeSpan Debounce = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan ResumeInterval = TimeSpan.FromMinutes(1);
    public static readonly IReadOnlyList<TimeSpan> Backoff =
        [TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(15)];

    private const string LastSyncedKey = "LastSyncedAt";

    private readonly SyncEngine _engine;
    private readonly ShowUpDatabase _db;
    private readonly IUserScope _scope;
    private readonly IEntitlementService _entitlements;
    private readonly DataChangeAnnouncer _announcer;
    private readonly IPreferences _preferences;
    private readonly TimeProvider _time;
    private readonly ILogger<SyncService> _logger;
    private readonly CloudSyncAvailability _availability;
    private readonly SynchronizationContext? _context = SynchronizationContext.Current;
    private readonly RefreshThrottle _resumeThrottle;
    private readonly ITimer _debounce;
    private readonly ITimer _retry;
    private readonly Lock _gate = new();
    private Task? _running;
    private CancellationTokenSource? _pass;
    private bool _again;
    private bool _started;
    private int _failures;
    private volatile SyncState _state;

    public SyncService(
        SyncEngine engine,
        ShowUpDatabase db,
        IUserScope scope,
        IEntitlementService entitlements,
        DataChangeAnnouncer announcer,
        IPreferences preferences,
        TimeProvider time,
        ILogger<SyncService> logger,
        CloudSyncAvailability availability)
    {
        _engine = engine;
        _db = db;
        _scope = scope;
        _entitlements = entitlements;
        _announcer = announcer;
        _preferences = preferences;
        _time = time;
        _logger = logger;
        _availability = availability;
        _resumeThrottle = new(time, ResumeInterval);
        _debounce = time.CreateTimer(_ => _ = RequestSyncAsync(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        _retry = time.CreateTimer(_ => _ = RequestSyncAsync(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        _state = Gate() ?? SyncState.Idle;
    }

    public SyncState State => _state;

    /// <summary>The last sync that finished, for the signed-in account on this device.</summary>
    public DateTimeOffset? LastSyncedAt =>
        _scope.CurrentUserId is { } uid && _preferences.Get(LastSyncedKey + AccountProCache.KeySuffix(uid), 0L) is var ticks and > 0
            ? new DateTimeOffset(ticks, TimeSpan.Zero)
            : null;

    /// <summary>On the thread that built the service when it had one.</summary>
    public event EventHandler? Changed;

    /// <summary>Call once at app start, after the session is restored.</summary>
    public Task StartAsync()
    {
        lock (_gate)
        {
            if (!_started)
            {
                _started = true;
                _db.LocalChanged += OnLocalChanged;
                _scope.Changed += OnScopeChanged;
                _entitlements.Changed += OnEntitlementsChanged;
            }
        }

        _resumeThrottle.TryEnter();
        return RequestSyncAsync();
    }

    /// <summary>The app came back to the foreground; at most once a minute.</summary>
    public Task ResumeAsync() => _resumeThrottle.TryEnter() ? RequestSyncAsync() : Task.CompletedTask;

    /// <summary>"Sync now": runs at once, whatever the backoff said. Completes when a sync that includes everything so far is done.</summary>
    public Task SyncNowAsync()
    {
        Interlocked.Exchange(ref _failures, 0);
        _retry.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        return RequestSyncAsync();
    }

    /// <summary>Completes when no sync is running (or queued to run again).</summary>
    public Task WaitForIdleAsync()
    {
        lock (_gate)
        {
            return _running ?? Task.CompletedTask;
        }
    }

    public async Task<bool> HasUnsyncedChangesAsync() =>
        Gate() is null && _scope.CurrentUserId is { } uid && await _engine.CountPendingAsync(uid) > 0;

    public void Dispose()
    {
        _db.LocalChanged -= OnLocalChanged;
        _scope.Changed -= OnScopeChanged;
        _entitlements.Changed -= OnEntitlementsChanged;
        _debounce.Dispose();
        _retry.Dispose();
    }

    // Single flight: a request while a sync runs makes it go round once more.
    private Task RequestSyncAsync()
    {
        lock (_gate)
        {
            if (_running is not null)
            {
                _again = true;
                return _running;
            }

            _running = RunLoopAsync();
            return _running;
        }
    }

    private async Task RunLoopAsync()
    {
        await Task.Yield();
        while (true)
        {
            try
            {
                await RunOnceAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Sync failed unexpectedly.");
            }

            lock (_gate)
            {
                if (!_again)
                {
                    _running = null;
                    return;
                }

                _again = false;
            }
        }
    }

    private async Task RunOnceAsync()
    {
        if (Gate() is { } gated)
        {
            SetState(gated);
            return;
        }

        string uid = _scope.CurrentUserId!;
        SetState(SyncState.Syncing);
        SyncProgress progress = new();
        using CancellationTokenSource pass = new();
        Volatile.Write(ref _pass, pass);
        try
        {
            await _engine.SyncAsync(uid, progress, () => _scope.CurrentUserId == uid, pass.Token);
            Interlocked.Exchange(ref _failures, 0);
            if (_scope.CurrentUserId == uid)
            {
                _preferences.Set(LastSyncedKey + AccountProCache.KeySuffix(uid), _time.GetUtcNow().UtcTicks);
                SetState(SyncState.Idle);
            }
        }
        // A request already out with the next account's token fails RLS instead: just as stale.
        catch (Exception ex) when (_scope.CurrentUserId != uid || (ex is OperationCanceledException && pass.IsCancellationRequested))
        {
            // The switch already asked for a sync of the new account.
            _logger.LogInformation("Sync stopped: the account switched ({Error}).", ex.GetType().Name);
        }
        catch (SupabaseException ex) when (ex.Kind is SupabaseErrorKind.Unauthorized or SupabaseErrorKind.NotSignedIn)
        {
            _logger.LogWarning("Sync stopped: the server no longer accepts the session ({Kind}).", ex.Kind);
            SetState(SyncState.SignInAgain);
        }
        catch (SupabaseException ex) when (ex.Kind is SupabaseErrorKind.Network or SupabaseErrorKind.Server or SupabaseErrorKind.RateLimited)
        {
            _logger.LogInformation("Sync couldn't reach the server ({Kind}); retrying later.", ex.Kind);
            ScheduleRetry(ex.RetryAfter);
            SetState(SyncState.Offline);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Sync failed; retrying later.");
            ScheduleRetry(null);
            SetState(SyncState.Failed);
        }
        finally
        {
            Interlocked.CompareExchange(ref _pass, null, pass);

            // An account switch meanwhile announces by itself.
            if (progress.Applied > 0 && _scope.CurrentUserId == uid)
            {
                Post(_announcer.Announce);
            }
        }
    }

    private SyncState? Gate()
    {
        if (!FeatureGate.IsAvailable(ProFeature.CloudSync, _availability))
        {
            return SyncState.Off;
        }

        if (_scope.CurrentUserId is null)
        {
            return SyncState.SignedOut;
        }

        return FeatureGate.CanUse(ProFeature.CloudSync, _entitlements.IsPro) ? null : SyncState.Paused;
    }

    private void ScheduleRetry(TimeSpan? retryAfter)
    {
        int failures = Interlocked.Increment(ref _failures);
        var delay = Backoff[Math.Min(failures, Backoff.Count) - 1];
        if (retryAfter > delay)
        {
            delay = retryAfter.Value;
        }

        _retry.Change(delay, Timeout.InfiniteTimeSpan);
    }

    private void OnLocalChanged(object? sender, EventArgs e) => _debounce.Change(Debounce, Timeout.InfiniteTimeSpan);

    private void OnScopeChanged(object? sender, EventArgs e)
    {
        try
        {
            Volatile.Read(ref _pass)?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // That pass just finished.
        }

        Interlocked.Exchange(ref _failures, 0);
        _ = RequestSyncAsync();
    }

    private void OnEntitlementsChanged(object? sender, EventArgs e)
    {
        var gated = Gate();
        if (gated is not null)
        {
            SetState(gated.Value);
        }
        else if (_state is SyncState.Paused)
        {
            _ = RequestSyncAsync();
        }
    }

    private void SetState(SyncState state)
    {
        _state = state;
        Post(() => Changed?.Invoke(this, EventArgs.Empty));
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
}
