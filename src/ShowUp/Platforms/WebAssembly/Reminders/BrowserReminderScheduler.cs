using System.Runtime.InteropServices.JavaScript;
using CommunityToolkit.Mvvm.Messaging;
using ShowUp.Core.Messages;
using ShowUp.Core.Reminders;

namespace ShowUp.Platforms.WebAssembly.Reminders;

/// <summary>
/// Browser notifications from timers in WasmScripts/Reminders.js: only while a tab is open. Timers cover the next
/// <see cref="Window"/>; an hourly refresh (and every replan on focus) moves the window along.
/// </summary>
public sealed partial class BrowserReminderScheduler : IReminderScheduler, IDisposable
{
    public static readonly TimeSpan Window = TimeSpan.FromHours(24);
    private static readonly TimeSpan _refresh = TimeSpan.FromHours(1);

    private readonly TimeProvider _time;
    private readonly ILogger<BrowserReminderScheduler> _logger;
    private readonly ITimer _timer;
    private readonly bool _available;
    private IReadOnlyList<PlannedReminder> _plan = [];

    public BrowserReminderScheduler(TimeProvider time, IMessenger messenger, ILogger<BrowserReminderScheduler> logger)
    {
        _time = time;
        _logger = logger;
        try
        {
            Js.SetOpenHandler(() => messenger.Send(new ReminderOpenedMessage()));
            _available = true;
        }
        catch (Exception ex)
        {
            // WasmScripts/Reminders.js didn't load: report unsupported rather than failing the whole service graph.
            _logger.LogWarning(ex, "Browser reminders unavailable");
        }

        _timer = time.CreateTimer(_ => Push(), null, _refresh, _refresh);
    }

    public bool IsSupported => _available && Js.Permission() != "unsupported";

    public ReminderDeliveryLimit Limit => ReminderDeliveryLimit.WhileTabOpen;

    public async Task<bool> RequestPermissionAsync() => _available && await Js.Request() == "granted";

    public Task<bool> IsDeniedAsync() => Task.FromResult(_available && Js.Permission() == "denied");

    public Task ReplaceAllAsync(IReadOnlyList<PlannedReminder> reminders)
    {
        _plan = reminders;
        Push();
        return Task.CompletedTask;
    }

    public void Dispose() => _timer.Dispose();

    private void Push()
    {
        if (!_available)
        {
            return;
        }

        var due = ReminderNotifications.Within(_plan, _time.GetUtcNow(), Window);
        var count = Js.Schedule(
            [.. due.Select(ReminderNotifications.Key)],
            [.. due.Select(r => r.Title)],
            [.. due.Select(r => r.Body)],
            [.. due.Select(r => (double)r.FireAt.ToUnixTimeMilliseconds())]);
        _logger.LogInformation("Browser reminders: {Count} timers in the next {Window}; next at {Next}", count, Window, due.FirstOrDefault()?.FireAt);
    }

    private static partial class Js
    {
        [JSImport("globalThis.ShowUpReminders.permission")]
        public static partial string Permission();

        [JSImport("globalThis.ShowUpReminders.request")]
        [return: JSMarshalAs<JSType.Promise<JSType.String>>]
        public static partial Task<string> Request();

        [JSImport("globalThis.ShowUpReminders.setOpenHandler")]
        public static partial void SetOpenHandler([JSMarshalAs<JSType.Function>] Action onOpen);

        [JSImport("globalThis.ShowUpReminders.schedule")]
        public static partial int Schedule(string[] keys, string[] titles, string[] bodies, double[] times);
    }
}
