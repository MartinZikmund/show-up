namespace ShowUp.Core.Entitlements;

/// <summary>Lets an action through at most once per interval; used so window activations don't hammer the store.</summary>
public sealed class RefreshThrottle(TimeProvider time, TimeSpan interval)
{
    public static readonly TimeSpan ActivationInterval = TimeSpan.FromSeconds(60);

    private readonly object _gate = new();
    private DateTimeOffset? _last;

    /// <summary>True (and starts a new interval) when the last pass is older than the interval or there was none.</summary>
    public bool TryEnter()
    {
        lock (_gate)
        {
            var now = time.GetUtcNow();
            if (_last is { } last && now - last < interval)
            {
                return false;
            }

            _last = now;
            return true;
        }
    }
}
