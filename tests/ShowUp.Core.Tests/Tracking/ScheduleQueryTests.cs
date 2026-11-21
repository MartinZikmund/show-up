using AwesomeAssertions;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Time.Testing;
using ShowUp.Core.Tracking;

namespace ShowUp.Core.Tests.Tracking;

[TestClass]
public sealed class ScheduleQueryTests
{
    private static readonly DateOnly _today = new(2026, 11, 21);

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 21, 10, 0, 0, TimeSpan.Zero));
    private readonly FakeCatalogService _catalog = new(TestCatalog.Program("p", 1, "run", "rest"), TestCatalog.Program("q", 1, "abs+run"));
    private readonly FakeTrackingRepository _repository;
    private readonly ActiveEnrollmentService _service;
    private readonly ScheduleQuery _query;

    public ScheduleQueryTests()
    {
        _time.SetLocalTimeZone(TimeZoneInfo.Utc);
        _repository = new(_time);
        _service = TestEnrollments.Create(_repository, _catalog, new WeakReferenceMessenger());
        _query = new(_service, _catalog);
    }

    [TestMethod]
    public async Task GetDue_TwoPrograms_OneEntryEachWithTheirDay()
    {
        var p = (await _repository.EnrollAsync("p", 1, "t", _today.AddDays(-1))).Id;
        var q = (await _repository.EnrollAsync("q", 1, "t", _today)).Id;
        await _service.CrossAsync(q, 0, 0);

        var due = await _query.GetDueAsync(_today);

        var byId = due.ToDictionary(d => d.Active.Id);
        byId[p].DayIndex.Should().Be(1);
        byId[p].HasOpenSessions.Should().BeFalse("day 2 is a rest day");
        byId[q].DayIndex.Should().Be(0);
        byId[q].Day!.CompletedSessions.Should().Equal(0);
        byId[q].HasOpenSessions.Should().BeTrue();
    }

    [TestMethod]
    public async Task GetDue_DateOutsideTheProgram_HasNoDay()
    {
        await _repository.EnrollAsync("q", 1, "t", _today.AddDays(5));

        var due = await _query.GetDueAsync(_today);

        due.Should().ContainSingle().Which.DayIndex.Should().BeNull();
    }

    [TestMethod]
    public async Task GetDue_Selected_ComesFirst()
    {
        var p = (await _repository.EnrollAsync("p", 1, "t", _today)).Id;
        _time.Advance(TimeSpan.FromSeconds(1));
        await _repository.EnrollAsync("q", 1, "t", _today);
        await _service.SelectAsync(p);

        var due = await _query.GetDueAsync(_today);

        due[0].Active.Id.Should().Be(p);
    }

    [TestMethod]
    public async Task GetDue_OptionalOnlyDay_HasNothingOpen()
    {
        FakeCatalogService catalog = new(TestCatalog.Program("o", 1, "run?+abs?"));
        var service = TestEnrollments.Create(_repository, catalog, new WeakReferenceMessenger());
        await _repository.EnrollAsync("o", 1, "t", _today);

        var due = await new ScheduleQuery(service, catalog).GetDueAsync(_today);

        var day = due.Single();
        day.Day!.CountsAsRest.Should().BeTrue();
        day.HasOpenSessions.Should().BeFalse();
    }

    [TestMethod]
    public async Task GetDue_GapDayFromAShift_HasNoDay()
    {
        var p = (await _repository.EnrollAsync("p", 1, "t", _today.AddDays(-1))).Id;
        await _service.ShiftAsync(p, _today, 2);

        var due = await _query.GetDueAsync(_today);

        due.Single().DayIndex.Should().BeNull("today is a gap left by the shift");
        (await _query.GetDueAsync(_today.AddDays(2))).Single().DayIndex.Should().Be(1);
    }
}
