using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging;
using ShowUp.Core.Entitlements;
using ShowUp.Core.Messages;
using ShowUp.Core.Storage;

namespace ShowUp.Core.Reminders;

/// <summary>
/// Keeps the scheduler's next 14 days in step with reminders, tracking and entitlements.
/// When Pro lapses with several reminders on, the earliest-created one stays on and the rest are
/// disabled but kept; regaining Pro doesn't switch them back on, the user decides.
/// </summary>
public sealed class ReminderCoordinator : IDisposable
{
    public const int PlanDays = 14;

    private readonly IReminderRepository _reminders;
    private readonly IReminderScheduler _scheduler;
    private readonly IReminderDayProvider _days;
    private readonly IEntitlementService _entitlements;
    private readonly IMessenger _messenger;
    private readonly TimeProvider _time;
    private readonly ReminderMessageBuilder _messages;
    private readonly ILogger<ReminderCoordinator> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Task? _firstRefresh;
    private int _started;
    private long _requested;
    private long _covered;

    public ReminderCoordinator(
        IReminderRepository reminders,
        IReminderScheduler scheduler,
        IReminderDayProvider days,
        IEntitlementService entitlements,
        IMessenger messenger,
        TimeProvider time,
        ReminderMessageBuilder messages,
        ILogger<ReminderCoordinator> logger)
    {
        _reminders = reminders;
        _scheduler = scheduler;
        _days = days;
        _entitlements = entitlements;
        _messenger = messenger;
        _time = time;
        _messages = messages;
        _logger = logger;
    }

    /// <summary>Starts listening and plans once. Call at app start.</summary>
    public Task StartAsync()
    {
        if (Interlocked.Exchange(ref _started, 1) == 0)
        {
            _messenger.Register<ReminderCoordinator, TrackingChangedMessage>(this, static (r, m) => r.ReplanInBackground());
            _messenger.Register<ReminderCoordinator, RemindersChangedMessage>(this, static (r, m) => r.ReplanInBackground());
            _entitlements.Changed += OnEntitlementsChanged;
        }

        return ReplanAsync();
    }

    public void Dispose()
    {
        _messenger.UnregisterAll(this);
        _entitlements.Changed -= OnEntitlementsChanged;
    }

    private void OnEntitlementsChanged(object? sender, EventArgs e) => ReplanInBackground();

    // async void: whatever escapes ReplanAsync (even a failing logger) would take the app down.
    private async void ReplanInBackground()
    {
        try
        {
            await ReplanAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Background reminder planning failed: {ex}");
        }
    }

    /// <summary>The app came back to the foreground.</summary>
    public Task ResumeAsync() => ReplanAsync();

    public async Task ReplanAsync()
    {
        var ticket = Interlocked.Increment(ref _requested);
        await _gate.WaitAsync();
        try
        {
            if (Interlocked.Read(ref _covered) >= ticket)
            {
                return;
            }

            var snapshot = Interlocked.Read(ref _requested);
            try
            {
                await RunAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Planning the reminders failed.");
            }

            Interlocked.Exchange(ref _covered, snapshot);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task RunAsync()
    {
        var all = await EnforceLimitAsync();
        if (!_scheduler.IsSupported)
        {
            return;
        }

        var now = _time.GetLocalNow();
        var zone = _time.LocalTimeZone;
        var day = await _days.LoadAsync(DateOnly.FromDateTime(now.DateTime), PlanDays);
        await _scheduler.ReplaceAllAsync(ReminderPlanner.Plan(all, now, zone, PlanDays, day, _messages));
    }

    private async Task<IReadOnlyList<ReminderEntity>> EnforceLimitAsync()
    {
        if (!_entitlements.IsPro)
        {
            // A cached "not Pro" at launch may be a renewal the store hasn't reported yet.
            _firstRefresh ??= RefreshQuietlyAsync();
            await _firstRefresh;
        }

        var all = await _reminders.ListAsync();
        var max = FeatureGate.MaxReminders(_entitlements.IsPro);
        var extra = all.Where(r => r.Enabled).OrderBy(r => r.CreatedAt).Skip(max).ToList();
        if (_entitlements.IsPro || extra.Count == 0)
        {
            return all;
        }

        foreach (var reminder in extra)
        {
            await _reminders.SetEnabledAsync(reminder.Id, false);
        }

        _messenger.Send(new RemindersChangedMessage());
        return await _reminders.ListAsync();
    }

    private async Task RefreshQuietlyAsync()
    {
        try
        {
            await _entitlements.RefreshAsync().WaitAsync(ExpiryWatch.RefreshTimeout, _time);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Refreshing Pro before the reminder check failed; using the cached state.");
        }
    }
}
