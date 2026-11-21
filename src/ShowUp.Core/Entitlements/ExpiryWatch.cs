namespace ShowUp.Core.Entitlements;

/// <summary>
/// Rechecks Pro when a cached subscription reaches its end mid-session: refreshes from the store first, so a
/// renewal keeps Pro, and reports a lapse only when the refresh still says not Pro (or couldn't reach the store).
/// </summary>
public sealed class ExpiryWatch(TimeProvider time) : IDisposable
{
    // Timers can't wait much past 49 days; longer waits re-arm in steps.
    private static readonly TimeSpan _maxStep = TimeSpan.FromDays(30);

    private readonly object _gate = new();
    private ITimer? _timer;
    private int _generation;

    /// <summary>Replaces any earlier schedule; a past or missing expiry schedules nothing.</summary>
    /// <param name="refreshAsync">Asks the store again; failures count as "still not Pro".</param>
    /// <param name="isPro">Pro as the refreshed cache says.</param>
    /// <param name="lapsed">Runs when the subscription really ended.</param>
    public void Schedule(DateTimeOffset? expires, Func<Task> refreshAsync, Func<bool> isPro, Action lapsed)
    {
        ArgumentNullException.ThrowIfNull(refreshAsync);
        ArgumentNullException.ThrowIfNull(isPro);
        ArgumentNullException.ThrowIfNull(lapsed);
        lock (_gate)
        {
            var generation = ++_generation;
            _timer?.Dispose();
            _timer = null;
            if (expires is not { } at || at <= time.GetUtcNow())
            {
                return;
            }

            var due = at - time.GetUtcNow();
            _timer = time.CreateTimer(
                _ => OnTimer(generation, at, refreshAsync, isPro, lapsed),
                null,
                due < _maxStep ? due : _maxStep,
                Timeout.InfiniteTimeSpan);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _generation++;
            _timer?.Dispose();
            _timer = null;
        }
    }

    private async void OnTimer(int generation, DateTimeOffset at, Func<Task> refreshAsync, Func<bool> isPro, Action lapsed)
    {
        if (Volatile.Read(ref _generation) != generation)
        {
            return;
        }

        if (time.GetUtcNow() < at)
        {
            Schedule(at, refreshAsync, isPro, lapsed);
            return;
        }

        try
        {
            await refreshAsync();
        }
        catch
        {
            // Offline: the cached expiry stands.
        }

        // The refresh may have re-armed the watch for a renewal; Pro decides, not the generation.
        if (!isPro())
        {
            lapsed();
        }
    }
}
