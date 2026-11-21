namespace ShowUp.Core.Entitlements;

/// <summary>Fires once when a cached subscription runs out mid-session, so Pro features lock without a restart.</summary>
public sealed class ExpiryWatch(TimeProvider time) : IDisposable
{
    // Timers can't wait much past 49 days; longer waits re-arm in steps.
    private static readonly TimeSpan _maxStep = TimeSpan.FromDays(30);

    private ITimer? _timer;

    /// <summary>Replaces any earlier schedule; a past or missing expiry schedules nothing.</summary>
    public void Schedule(DateTimeOffset? expires, Action onExpired)
    {
        ArgumentNullException.ThrowIfNull(onExpired);
        Dispose();
        if (expires is not { } at || at <= time.GetUtcNow())
        {
            return;
        }

        var due = at - time.GetUtcNow();
        _timer = time.CreateTimer(
            _ =>
            {
                if (time.GetUtcNow() >= at)
                {
                    onExpired();
                }
                else
                {
                    Schedule(at, onExpired);
                }
            },
            null,
            due < _maxStep ? due : _maxStep,
            Timeout.InfiniteTimeSpan);
    }

    public void Dispose()
    {
        _timer?.Dispose();
        _timer = null;
    }
}
