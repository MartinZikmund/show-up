using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using ShowUp.Core.Reminders;

namespace ShowUp.Core.Tests.Reminders;

[TestClass]
public sealed class InAppReminderSchedulerTests : IDisposable
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 21, 17, 0, 0, TimeSpan.Zero));
    private readonly InAppReminderScheduler _scheduler;
    private readonly List<PlannedReminder> _shown = [];

    public InAppReminderSchedulerTests()
    {
        _scheduler = new(_time, NullLogger<InAppReminderScheduler>.Instance);
        _scheduler.Due += (_, r) => _shown.Add(r);
    }

    public void Dispose() => _scheduler.Dispose();

    private PlannedReminder In(TimeSpan delay) => new(Guid.NewGuid(), _time.GetUtcNow() + delay, "Show Up", "Run is waiting. Show up.");

    [TestMethod]
    public async Task Tick_ReminderComesDue_RaisesItOnce()
    {
        var reminder = In(TimeSpan.FromMinutes(1));
        await _scheduler.ReplaceAllAsync([reminder]);

        _time.Advance(TimeSpan.FromSeconds(45));
        _shown.Should().BeEmpty();
        _time.Advance(TimeSpan.FromSeconds(30));
        _time.Advance(TimeSpan.FromMinutes(5));

        _shown.Should().Equal(reminder);
        _scheduler.Pending.Should().BeEmpty();
    }

    [TestMethod]
    public async Task Tick_MachineSleptPastTheTolerance_DropsItQuietly()
    {
        await _scheduler.ReplaceAllAsync([In(TimeSpan.FromMinutes(1))]);

        // One jump, as after waking from sleep: the first tick sees it 30 minutes late.
        _time.Advance(TimeSpan.FromMinutes(31));

        _shown.Should().BeEmpty();
        _scheduler.Pending.Should().BeEmpty();
    }

    [TestMethod]
    public async Task ReplaceAll_ReminderCameDueSinceTheLastTick_StillShowsIt()
    {
        var reminder = In(TimeSpan.FromSeconds(5));
        await _scheduler.ReplaceAllAsync([reminder]);
        _time.AdjustTime(_time.GetUtcNow() + TimeSpan.FromSeconds(8));

        // The planner never hands back a past time, so the replan would lose it without a check first.
        await _scheduler.ReplaceAllAsync([]);

        _shown.Should().Equal(reminder);
    }

    [TestMethod]
    public async Task ReplaceAll_NewPlan_DropsTheOldOne()
    {
        await _scheduler.ReplaceAllAsync([In(TimeSpan.FromMinutes(1))]);
        var replacement = In(TimeSpan.FromMinutes(2));

        await _scheduler.ReplaceAllAsync([replacement]);
        _time.Advance(TimeSpan.FromMinutes(3));

        _shown.Should().Equal(replacement);
    }

    [TestMethod]
    public async Task Due_HandlerThrows_KeepsTicking()
    {
        _scheduler.Due += (_, _) => throw new InvalidOperationException("no window");
        await _scheduler.ReplaceAllAsync([In(TimeSpan.FromSeconds(10)), In(TimeSpan.FromMinutes(2))]);

        _time.Advance(TimeSpan.FromMinutes(3));

        _shown.Should().HaveCount(2);
    }

    [TestMethod]
    public void Limit_IsWhileTheAppRuns()
    {
        _scheduler.IsSupported.Should().BeTrue();
        _scheduler.Limit.Should().Be(ReminderDeliveryLimit.WhileAppRunning);
    }
}
