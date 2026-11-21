namespace ShowUp.Core.Entitlements;

/// <summary>
/// Runs one operation at a time. Callers arriving before it starts share it; callers arriving while it
/// runs share one trailing run, so a refresh asked for after a purchase never returns older state.
/// </summary>
public sealed class SingleFlight
{
    private readonly object _gate = new();
    private Task? _running;
    private bool _started;
    private Task? _trailing;

    public Task RunAsync(Func<Task> work)
    {
        lock (_gate)
        {
            if (_running is null)
            {
                _started = false;
                return _running = Wrap(work);
            }

            return _started ? _trailing ??= AfterAsync(_running, work) : _running;
        }
    }

    private async Task Wrap(Func<Task> work)
    {
        try
        {
            await Task.Yield();
            lock (_gate)
            {
                _started = true;
            }

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

    private async Task AfterAsync(Task previous, Func<Task> work)
    {
        try
        {
            await previous;
        }
        catch
        {
            // The earlier run's failure belongs to its callers.
        }

        lock (_gate)
        {
            _trailing = null;
        }

        await RunAsync(work);
    }
}
