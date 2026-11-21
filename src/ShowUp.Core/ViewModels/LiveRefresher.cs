using Microsoft.Extensions.Logging;

namespace ShowUp.Core.ViewModels;

/// <summary>
/// The plumbing a page that mirrors tracking data shares: a timer for the next day (or greeting) change,
/// an own-write guard so the echoed <c>TrackingChangedMessage</c> doesn't reload twice, and
/// fire-and-forget loads that log instead of crashing.
/// </summary>
/// <param name="nextChangeIn">Time from now until the page's content next changes by itself; one second of slack is added.</param>
public sealed class LiveRefresher(TimeProvider time, ILogger logger, string failureMessage, Func<Task> refresh, Func<DateTimeOffset, TimeSpan> nextChangeIn)
{
    private SynchronizationContext? _context;
    private ITimer? _timer;
    private int _ownWrites;

    public bool IsActive { get; private set; }

    /// <summary>Marks the page active and remembers its UI thread; false when it already was.</summary>
    public bool Activate()
    {
        if (IsActive)
        {
            return false;
        }

        IsActive = true;
        _context = SynchronizationContext.Current;
        return true;
    }

    public void Deactivate()
    {
        IsActive = false;
        StopTimer();
    }

    public void ScheduleNextChange()
    {
        StopTimer();
        if (IsActive)
        {
            var due = nextChangeIn(time.GetLocalNow()) + TimeSpan.FromSeconds(1);
            _timer = time.CreateTimer(_ => Post(() => RunInBackground(refresh)), null, due, Timeout.InfiniteTimeSpan);
        }
    }

    /// <summary>For the tracking message: reloads, unless this page's own write caused it (it refreshes itself).</summary>
    public void OnTrackingChanged()
    {
        if (Volatile.Read(ref _ownWrites) == 0)
        {
            Post(() => RunInBackground(refresh));
        }
    }

    /// <summary>
    /// For the data-changed message: reloads even for own writes. <c>DataChangeAnnouncer</c> dropped the service caches before
    /// sending it, so this needn't wait for the services' own handlers (the messenger calls recipients in no set order).
    /// </summary>
    public void OnDataChanged() => Post(() => RunInBackground(refresh));

    public async Task WriteAsync(Func<Task> write)
    {
        Interlocked.Increment(ref _ownWrites);
        try
        {
            await write();
        }
        finally
        {
            Interlocked.Decrement(ref _ownWrites);
        }
    }

    // Fire-and-forget loads must never take the app down; log and keep showing the last good state.
    public async void RunInBackground(Func<Task> work)
    {
        try
        {
            await work();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "{Message}", failureMessage);
        }
    }

    // Timers and messages can arrive off the UI thread; bound state must change on it.
    public void Post(Action action)
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

    private void StopTimer()
    {
        _timer?.Dispose();
        _timer = null;
    }
}
