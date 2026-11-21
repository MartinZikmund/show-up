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
        await _scheduler.WaitForAttemptsAsync(count);
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
    public async Task Replan_LapseSeenOnlyInTheCache_KeepsEveryReminderOn()
    {
        await _repository.AddAsync(8 * 60, 127);
        var second = await _repository.AddAsync(9 * 60, 127);
        _entitlements.IsPro = false;
        _entitlements.IsConfirmed = false;

        await _coordinator.StartAsync();

        (await _repository.ListAsync()).Single(r => r.Id == second.Id).Enabled.Should().BeTrue("the store never answered, so the lapse isn't proven");
        _repository.SetEnabledCalls.Should().Be(0);
    }

    [TestMethod]
    public async Task Replan_LapseTheStoreConfirmsAfterRefresh_Disables()
    {
        await _repository.AddAsync(8 * 60, 127);
        var second = await _repository.AddAsync(9 * 60, 127);
        _entitlements.IsPro = false;
        _entitlements.IsConfirmed = false;
        _entitlements.OnRefresh = () =>
        {
            _entitlements.IsConfirmed = true;
            return Task.CompletedTask;
        };

        await _coordinator.StartAsync();

        (await _repository.ListAsync()).Single(r => r.Id == second.Id).Enabled.Should().BeFalse();
    }

    [TestMethod]
    public async Task Replan_DisablingExtras_DoesNotReplanOnItsOwnMessage()
    {
        await _repository.AddAsync(8 * 60, 127);
        await _repository.AddAsync(9 * 60, 127);
        _entitlements.IsPro = false;
        var sent = 0;
        _messenger.Register<RemindersChangedMessage>(this, (_, _) => sent++);

        await _coordinator.StartAsync();

        sent.Should().Be(1, "the disabled reminders are announced to the Reminders page");
        _days.Loads.Should().Be(1, "but the coordinator doesn't plan again because of its own announcement");
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
    public async Task BackgroundReplan_SchedulerAndLoggerThrow_DoesNotEscape()
    {
        await _repository.AddAsync(8 * 60, 127);
        using ReminderCoordinator coordinator = new(_repository, _scheduler, _days, _entitlements, _messenger, _time, EnglishReminderLocalizer.Messages, new ThrowingLogger<ReminderCoordinator>());
        await coordinator.StartAsync();
        _scheduler.FailReplace = true;

        _messenger.Send(new TrackingChangedMessage());
        await _scheduler.WaitForAttemptsAsync(2);
        _scheduler.FailReplace = false;
        await coordinator.ReplanAsync();

        _scheduler.Replaced.Should().HaveCount(2, "the start plan and the one after the failure");
    }

    [TestMethod]
    public async Task Dispose_StopsListening()
    {
        await _coordinator.StartAsync();
        _coordinator.Dispose();

        _messenger.Send(new TrackingChangedMessage());
        _messenger.Send(new RemindersChangedMessage());
        _entitlements.SetPro(true);

        _scheduler.Replaced.Should().ContainSingle("a listening coordinator would have planned by now: the fakes complete synchronously");
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

        _scheduler.Replaced.Should().ContainSingle("a second listener would have planned by now: the fakes complete synchronously");
    }
}

internal sealed class ThrowingLogger<T> : Microsoft.Extensions.Logging.ILogger<T>
{
    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;

    public void Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel, Microsoft.Extensions.Logging.EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
        throw new InvalidOperationException("logging is broken");
}
