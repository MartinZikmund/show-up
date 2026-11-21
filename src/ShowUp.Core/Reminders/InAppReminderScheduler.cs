using Microsoft.Extensions.Logging;

namespace ShowUp.Core.Reminders;

/// <summary>
/// Delivers reminders inside the running app: checks the wall clock every <see cref="TickInterval"/>
/// (robust to sleep and clock changes) and raises <see cref="Due"/>. Nothing fires once the app closes.
/// </summary>
public sealed class InAppReminderScheduler : IReminderScheduler, IDisposable
{
    public static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(15);

    /// <summary>A reminder found later than this (the machine slept through it) is dropped, not shown stale.</summary>
    public static readonly TimeSpan LateTolerance = TimeSpan.FromMinutes(10);

    private readonly TimeProvider _time;
    private readonly ILogger<InAppReminderScheduler> _logger;
    private readonly Lock _lock = new();
    private readonly ITimer _timer;
    private List<PlannedReminder> _pending = [];

    public InAppReminderScheduler(TimeProvider time, ILogger<InAppReminderScheduler> logger)
    {
        _time = time;
        _logger = logger;
        _timer = time.CreateTimer(_ => Check(), null, TickInterval, TickInterval);
    }

    /// <summary>Raised on the timer's thread; marshal to the UI before touching it.</summary>
    public event EventHandler<PlannedReminder>? Due;

    public bool IsSupported => true;

    public ReminderDeliveryLimit Limit => ReminderDeliveryLimit.WhileAppRunning;

    public IReadOnlyList<PlannedReminder> Pending
    {
        get
        {
            lock (_lock)
            {
                return [.. _pending];
            }
        }
    }

    public Task<bool> RequestPermissionAsync() => Task.FromResult(true);

    public Task<bool> IsDeniedAsync() => Task.FromResult(false);

    public Task ReplaceAllAsync(IReadOnlyList<PlannedReminder> reminders)
    {
        // The new plan only holds future times: deliver anything that came due since the last tick first.
        Check();
        var now = _time.GetUtcNow();
        List<PlannedReminder> pending = [.. reminders.Where(r => r.FireAt > now).OrderBy(r => r.FireAt)];
        lock (_lock)
        {
            _pending = pending;
        }

        _logger.LogInformation("In-app reminders: {Count} pending, next at {Next}", pending.Count, pending.FirstOrDefault()?.FireAt);
        return Task.CompletedTask;
    }

    public void Check()
    {
        var now = _time.GetUtcNow();
        List<PlannedReminder> due;
        lock (_lock)
        {
            due = [.. _pending.Where(r => r.FireAt <= now)];
            if (due.Count == 0)
            {
                return;
            }

            _pending = [.. _pending.Where(r => r.FireAt > now)];
        }

        foreach (var reminder in due)
        {
            if (now - reminder.FireAt > LateTolerance)
            {
                _logger.LogInformation("Skipping a reminder for {FireAt}: the app wasn't checking then", reminder.FireAt);
                continue;
            }

            try
            {
                Due?.Invoke(this, reminder);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Showing an in-app reminder failed.");
            }
        }
    }

    public void Dispose() => _timer.Dispose();
}
