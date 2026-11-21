namespace ShowUp.Core.Entitlements;

/// <summary>
/// Keeps an account-based store's refreshes and account switches apart: one at a time, so a refresh for the previous
/// account never lands in the next one's cache. Calls that can't wait (a purchase) check <see cref="IsCurrent"/> instead.
/// </summary>
public sealed class StoreAccountGate
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private long _generation;

    /// <summary>Bumped by every switch.</summary>
    public long Generation => Interlocked.Read(ref _generation);

    public bool IsCurrent(long generation) => generation == Generation;

    public async Task RunAsync(Func<Task> work)
    {
        await _gate.WaitAsync();
        try
        {
            await work();
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task SwitchAsync(Func<Task> work) => RunAsync(() =>
    {
        Interlocked.Increment(ref _generation);
        return work();
    });
}
