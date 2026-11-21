using AwesomeAssertions;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using ShowUp.Core.Messages;
using ShowUp.Core.Storage;
using ShowUp.Core.Tracking;

namespace ShowUp.Core.Tests.Tracking;

[TestClass]
public sealed class ActiveEnrollmentServiceTests
{
    private static readonly DateOnly _today = new(2026, 11, 21);

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 21, 10, 0, 0, TimeSpan.Zero));
    private readonly FakeCatalogService _catalog = new(
        TestCatalog.Program("p", 1, "run", "run", "run"),
        TestCatalog.Program("p", 2, "run", "run", "run", "run", "run"));

    private readonly WeakReferenceMessenger _messenger = new();
    private readonly FakeTrackingRepository _repository;
    private readonly ActiveEnrollmentService _service;

    public ActiveEnrollmentServiceTests()
    {
        _time.SetLocalTimeZone(TimeZoneInfo.Utc);
        _repository = new(_time);
        _service = new(_repository, _catalog, _messenger, NullLogger<ActiveEnrollmentService>.Instance);
    }

    [TestMethod]
    public async Task GetAsync_NoEnrollment_ReturnsNull()
    {
        var active = await _service.GetAsync();

        active.Should().BeNull();
    }

    [TestMethod]
    public async Task GetAsync_PinnedRevision_ResolvesThatRevision()
    {
        await _repository.EnrollAsync("p", 1, "t", _today);

        var active = await _service.GetAsync();

        active!.Program.Revision.Should().Be(1);
        active.Calendar.DayCount.Should().Be(3);
    }

    [TestMethod]
    public async Task GetAsync_PinnedRevisionMissing_FallsBackToLatest()
    {
        await _repository.EnrollAsync("p", 7, "t", _today);

        var active = await _service.GetAsync();

        active!.Program.Revision.Should().Be(2);
        active.Calendar.DayCount.Should().Be(5);
    }

    [TestMethod]
    public async Task CrossAsync_ShiftedSchedule_UsesCalendarDate()
    {
        await _repository.EnrollAsync("p", 1, "t", _today.AddDays(-5));
        await _repository.ShiftScheduleAsync(Guid.Empty, _today.AddDays(-4), 2, 3);

        await _service.CrossAsync(1, 0);

        _repository.Crosses.Should().ContainSingle().Which.Date.Should().Be(_today.AddDays(-2));
    }

    [TestMethod]
    public async Task CrossAsync_WithInk_PersistsInk()
    {
        await _repository.EnrollAsync("p", 1, "t", _today);

        await _service.CrossAsync(0, 0, "[[0,0,1,1]]");

        _repository.Crosses.Should().ContainSingle().Which.Ink.Should().Be("[[0,0,1,1]]");
    }

    [TestMethod]
    public async Task ShiftAsync_ActiveEnrollment_PassesTrackDayCount()
    {
        await _repository.EnrollAsync("p", 2, "t", _today);

        await _service.ShiftAsync(_today, 3);

        _repository.ShiftCalls.Should().Equal((_today, 3, 5));
        (await _service.GetAsync())!.Calendar.DateOf(0).Should().Be(_today.AddDays(3));
    }

    [TestMethod]
    public async Task CrossAsync_Crossed_RaisesChangedAndSendsMessage()
    {
        await _repository.EnrollAsync("p", 1, "t", _today);
        var changed = 0;
        var messages = 0;
        _service.Changed += (_, _) => changed++;
        _messenger.Register<TrackingChangedMessage>(this, (_, _) => messages++);

        await _service.CrossAsync(0, 0);

        changed.Should().Be(1);
        messages.Should().Be(1);
    }

    [TestMethod]
    public async Task EndAsync_ActiveEnrollment_LeavesNoActiveEnrollment()
    {
        await _repository.EnrollAsync("p", 1, "t", _today);
        await _service.GetAsync();

        await _service.EndAsync(EnrollmentEndReason.Abandoned);

        (await _service.GetAsync()).Should().BeNull();
    }

    [TestMethod]
    public async Task GetBonusWorkoutsAsync_BonusOutsideCalendar_IsExcluded()
    {
        await _repository.EnrollAsync("p", 1, "t", _today);
        await _service.LogBonusAsync(null, "Inside", _today.AddDays(1), null);
        await _repository.LogBonusAsync(null, "Outside", _today.AddDays(10), null);

        var bonuses = await _service.GetBonusWorkoutsAsync();

        bonuses.Select(b => b.Title).Should().Equal("Inside");
    }
}
