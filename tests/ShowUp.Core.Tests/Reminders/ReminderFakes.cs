using ShowUp.Core.Reminders;
using ShowUp.Core.Storage;

namespace ShowUp.Core.Tests.Reminders;

internal sealed class FakeReminderRepository : IReminderRepository
{
    private readonly List<ReminderEntity> _items = [];
    private long _ticks;

    public int SetEnabledCalls { get; private set; }

    public Task<IReadOnlyList<ReminderEntity>> ListAsync() =>
        Task.FromResult<IReadOnlyList<ReminderEntity>>([.. _items.OrderBy(r => r.MinutesOfDay).ThenBy(r => r.CreatedAt)]);

    public Task<ReminderEntity> AddAsync(int minutesOfDay, int daysMask, bool enabled = true)
    {
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
        var item = _items.Single(r => r.Id == id);
        item.MinutesOfDay = minutesOfDay;
        item.DaysMask = daysMask;
        return Task.CompletedTask;
    }

    public Task SetEnabledAsync(Guid id, bool enabled)
    {
        SetEnabledCalls++;
        _items.Single(r => r.Id == id).Enabled = enabled;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(Guid id)
    {
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

    public Task<bool> RequestPermissionAsync()
    {
        PermissionRequests++;
        return Task.FromResult(PermissionResult);
    }

    public Task ReplaceAllAsync(IReadOnlyList<PlannedReminder> reminders)
    {
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
