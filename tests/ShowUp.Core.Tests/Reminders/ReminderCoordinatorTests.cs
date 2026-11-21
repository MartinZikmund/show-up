using AwesomeAssertions;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using ShowUp.Core.Messages;
using ShowUp.Core.Reminders;
using ShowUp.Core.Tests.Entitlements;

namespace ShowUp.Core.Tests.Reminders;

[TestClass]
public sealed class ReminderCoordinatorTests
{
    private readonly FakeReminderRepository _repository = new();
    private readonly FakeReminderScheduler _scheduler = new();
    private readonly FakeDayProvider _days = new();
    private readonly FakeEntitlementService _entitlements = new() { IsPro = true };
    private readonly WeakReferenceMessenger _messenger = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 21, 10, 0, 0, TimeSpan.Zero));
    private readonly ReminderCoordinator _coordinator;

    public ReminderCoordinatorTests()
    {
        _time.SetLocalTimeZone(TimeZoneInfo.Utc);
        _coordinator = new(_repository, _scheduler, _days, _entitlements, _messenger, _time, EnglishReminderLocalizer.Messages, NullLogger<ReminderCoordinator>.Instance);
    }

    private async Task WaitForPlansAsync(int count)
    {
        for (var i = 0; i < 200 && _scheduler.Replaced.Count < count; i++)
        {
            await Task.Delay(5);
        }

        _scheduler.Replaced.Should().HaveCountGreaterThanOrEqualTo(count);
    }

    [TestMethod]
    public async Task Start_PlansTheNextFourteenDays()
    {
        await _repository.AddAsync(8 * 60, 127);

        await _coordinator.StartAsync();

        _scheduler.Replaced.Should().ContainSingle().Which.Should().HaveCount(13);
    }

    [TestMethod]
    public async Task Resume_PlansAgain()
    {
        await _repository.AddAsync(8 * 60, 127);
        await _coordinator.StartAsync();

        await _coordinator.ResumeAsync();

        _scheduler.Replaced.Should().HaveCount(2);
    }

    [TestMethod]
    public async Task TrackingChanged_Replans()
    {
        await _repository.AddAsync(8 * 60, 127);
        await _coordinator.StartAsync();

        _messenger.Send(new TrackingChangedMessage());

        await WaitForPlansAsync(2);
    }

    [TestMethod]
    public async Task RemindersChanged_Replans()
    {
        await _coordinator.StartAsync();

        await _repository.AddAsync(8 * 60, 127);
        _messenger.Send(new RemindersChangedMessage());

        await WaitForPlansAsync(2);
        _scheduler.Replaced[^1].Should().NotBeEmpty();
    }

    [TestMethod]
    public async Task EntitlementChanged_Replans()
    {
        await _coordinator.StartAsync();

        _entitlements.SetPro(true);

        await WaitForPlansAsync(2);
    }

    [TestMethod]
    public async Task Replan_CompletedDay_LeavesItOut()
    {
        await _repository.AddAsync(18 * 60, 127);
        var today = DateOnly.FromDateTime(_time.GetLocalNow().DateTime);
        _days.Day = d => new ReminderDay(d == today, "Run", false);

        await _coordinator.StartAsync();

        _scheduler.Replaced.Single().Should().HaveCount(13).And.OnlyContain(p => DateOnly.FromDateTime(p.FireAt.DateTime) != today);
    }

    [TestMethod]
    public async Task Replan_Unsupported_DoesNotSchedule()
    {
        _scheduler.IsSupported = false;
        await _repository.AddAsync(8 * 60, 127);

        await _coordinator.StartAsync();

        _scheduler.Replaced.Should().BeEmpty();
        _days.Loads.Should().Be(0);
    }

    [TestMethod]
    public async Task Replan_Lapsed_KeepsTheEarliestCreatedAndDisablesTheRest()
    {
        var first = await _repository.AddAsync(20 * 60, 127);
        var second = await _repository.AddAsync(7 * 60, 127);
        var third = await _repository.AddAsync(9 * 60, 127);
        _entitlements.IsPro = false;

        await _coordinator.StartAsync();

        var stored = (await _repository.ListAsync()).ToDictionary(r => r.Id);
        stored[first.Id].Enabled.Should().BeTrue("it was created first, whatever the time");
        stored[second.Id].Enabled.Should().BeFalse();
        stored[third.Id].Enabled.Should().BeFalse();
        stored.Should().HaveCount(3, "nothing is deleted");
        _scheduler.Replaced.Last().Select(p => p.ReminderId).Distinct().Should().Equal(first.Id);
    }

    [TestMethod]
    public async Task Replan_PaidAgain_DoesNotSwitchDisabledOnesBackOn()
    {
        var first = await _repository.AddAsync(8 * 60, 127);
        var second = await _repository.AddAsync(9 * 60, 127);
        _entitlements.IsPro = false;
        await _coordinator.StartAsync();

        _entitlements.SetPro(true);
        await _coordinator.ReplanAsync();

        (await _repository.ListAsync()).Single(r => r.Id == second.Id).Enabled.Should().BeFalse();
        (await _repository.ListAsync()).Single(r => r.Id == first.Id).Enabled.Should().BeTrue();
    }

    [TestMethod]
    public async Task Replan_ProWithManyEnabled_ChangesNothing()
    {
        await _repository.AddAsync(8 * 60, 127);
        await _repository.AddAsync(9 * 60, 127);

        await _coordinator.StartAsync();

        _repository.SetEnabledCalls.Should().Be(0);
    }

    [TestMethod]
    public async Task Replan_LapseWaitsForTheStoreBeforeDisabling()
    {
        await _repository.AddAsync(8 * 60, 127);
        var second = await _repository.AddAsync(9 * 60, 127);
        _entitlements.IsPro = false;
        _entitlements.OnRefresh = () =>
        {
            _entitlements.IsPro = true;
            return Task.CompletedTask;
        };

        await _coordinator.StartAsync();

        (await _repository.ListAsync()).Single(r => r.Id == second.Id).Enabled.Should().BeTrue("the store said the subscription renewed");
    }

    [TestMethod]
    public async Task Replan_PlanningThrows_DoesNotBreakLaterPlans()
    {
        await _repository.AddAsync(8 * 60, 127);
        _days.Day = _ => throw new InvalidOperationException("boom");
        await _coordinator.StartAsync();
        _days.Day = _ => new ReminderDay(false, "Run", false);

        await _coordinator.ReplanAsync();

        _scheduler.Replaced.Should().ContainSingle();
    }

    [TestMethod]
    public async Task Dispose_StopsListening()
    {
        await _coordinator.StartAsync();
        _coordinator.Dispose();

        _messenger.Send(new TrackingChangedMessage());
        _messenger.Send(new RemindersChangedMessage());
        _entitlements.SetPro(true);
        await Task.Delay(50);

        _scheduler.Replaced.Should().ContainSingle();
    }

    [TestMethod]
    public async Task Start_Twice_ListensOnce()
    {
        await _repository.AddAsync(8 * 60, 127);
        await _coordinator.StartAsync();
        await _coordinator.StartAsync();
        _scheduler.Replaced.Clear();

        _messenger.Send(new TrackingChangedMessage());
        await WaitForPlansAsync(1);
        await Task.Delay(50);

        _scheduler.Replaced.Should().ContainSingle();
    }
}
