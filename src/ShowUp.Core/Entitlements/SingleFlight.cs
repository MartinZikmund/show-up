namespace ShowUp.Core.Entitlements;

/// <summary>Runs one operation at a time; callers arriving meanwhile share the running one.</summary>
public sealed class SingleFlight
{
    private readonly object _gate = new();
    private Task? _running;

    public Task RunAsync(Func<Task> work)
    {
        lock (_gate)
        {
            return _running ??= Wrap(work);
        }
    }

    private async Task Wrap(Func<Task> work)
    {
        try
        {
            await Task.Yield();
            await work();
        }
        finally
        {
            lock (_gate)
            {
                _running = null;
            }
        }
    }
}
