using Microsoft.Extensions.Localization;
using ShowUp.Core.Reminders;
using ShowUp.Core.Storage;

namespace ShowUp.Core.Tests.Reminders;

internal sealed class FakeReminderRepository : IReminderRepository
{
    private readonly List<ReminderEntity> _items = [];
    private long _ticks;

    public int SetEnabledCalls { get; private set; }

    public int ListCalls { get; private set; }

    /// <summary>Makes every write throw.</summary>
    public bool FailWrites { get; set; }

    private void ThrowIfFailing()
    {
        if (FailWrites)
        {
            throw new InvalidOperationException("disk full");
        }
    }

    public Task<IReadOnlyList<ReminderEntity>> ListAsync()
    {
        ListCalls++;
        return Task.FromResult<IReadOnlyList<ReminderEntity>>([.. _items.OrderBy(r => r.MinutesOfDay).ThenBy(r => r.CreatedAt)]);
    }

    public Task<ReminderEntity> AddAsync(int minutesOfDay, int daysMask, bool enabled = true)
    {
        ThrowIfFailing();
        ReminderEntity reminder = new()
        {
            Id = Guid.NewGuid(),
            MinutesOfDay = minutesOfDay,
            DaysMask = daysMask,
            Enabled = enabled,
            CreatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero).AddSeconds(_ticks++),
        };
        _items.Add(reminder);
        return Task.FromResult(reminder);
    }

    public Task UpdateAsync(Guid id, int minutesOfDay, int daysMask)
    {
        ThrowIfFailing();
        var item = _items.Single(r => r.Id == id);
        item.MinutesOfDay = minutesOfDay;
        item.DaysMask = daysMask;
        return Task.CompletedTask;
    }

    public Task SetEnabledAsync(Guid id, bool enabled)
    {
        ThrowIfFailing();
        SetEnabledCalls++;
        _items.Single(r => r.Id == id).Enabled = enabled;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(Guid id)
    {
        ThrowIfFailing();
        _items.RemoveAll(r => r.Id == id);
        return Task.CompletedTask;
    }
}

internal sealed class FakeReminderScheduler : IReminderScheduler
{
    public bool IsSupported { get; set; } = true;

    public bool PermissionResult { get; set; } = true;

    public int PermissionRequests { get; private set; }

    public List<IReadOnlyList<PlannedReminder>> Replaced { get; } = [];

    /// <summary>Set to hold the permission prompt open until it is completed.</summary>
    public TaskCompletionSource<bool>? PendingPermission { get; set; }

    /// <summary>Makes every <see cref="ReplaceAllAsync"/> throw.</summary>
    public bool FailReplace { get; set; }

    public ReminderDeliveryLimit Limit { get; set; }

    public Task<bool> RequestPermissionAsync()
    {
        PermissionRequests++;
        return PendingPermission?.Task ?? Task.FromResult(PermissionResult);
    }

    public Task ReplaceAllAsync(IReadOnlyList<PlannedReminder> reminders)
    {
        if (FailReplace)
        {
            throw new InvalidOperationException("scheduler down");
        }

        Replaced.Add(reminders);
        return Task.CompletedTask;
    }
}

internal sealed class FakeDayProvider : IReminderDayProvider
{
    public Func<DateOnly, ReminderDay> Day { get; set; } = _ => new ReminderDay(false, "Run", false);

    public int Loads { get; private set; }

    public Task<Func<DateOnly, ReminderDay>> LoadAsync(DateOnly from, int days)
    {
        Loads++;
        return Task.FromResult(Day);
    }
}

/// <summary>The reminder texts as the English resources have them.</summary>
internal sealed class EnglishReminderLocalizer : IStringLocalizer
{
    private static readonly Dictionary<string, string> _texts = new()
    {
        ["ReminderTitle"] = "Show Up",
        ["ReminderWorkoutBody"] = "{0} is waiting. Show up.",
        ["ReminderRestBody"] = "Rest day. You earned it.",
    };

    public static ReminderMessageBuilder Messages { get; } = new(new EnglishReminderLocalizer());

    public LocalizedString this[string name] => this[name, []];

    public LocalizedString this[string name, params object[] arguments] =>
        _texts.TryGetValue(name, out var text) ? new(name, string.Format(text, arguments)) : new(name, name, resourceNotFound: true);

    public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];
}
