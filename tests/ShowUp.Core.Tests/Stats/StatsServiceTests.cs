using System.Globalization;
using AwesomeAssertions;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Time.Testing;
using ShowUp.Core.Catalog;
using ShowUp.Core.Stats;
using ShowUp.Core.Storage;
using ShowUp.Core.Tests.Entitlements;
using ShowUp.Core.Tests.Tracking;

namespace ShowUp.Core.Tests.Stats;

[TestClass]
public sealed class StatsServiceTests
{
    // A Saturday.
    private static readonly DateOnly _today = new(2026, 11, 21);

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 21, 10, 0, 0, TimeSpan.Zero));
    private readonly FakeTrackingRepository _repository;
    private readonly FakeEntitlementService _entitlements = new() { IsPro = true };
    private readonly FakeCatalogService _catalog = new(
        TestCatalog.Program("old", 1, "run", "run", "abs", "run", "run", "rest", "rest"),
        TestCatalog.Program("new", 1, "run", "rest", "abs", "run", "run", "rest", "rest"));

    private readonly CultureInfo _culture = CultureInfo.CurrentCulture;

    public StatsServiceTests()
    {
        CultureInfo.CurrentCulture = new CultureInfo("en-GB");
        _time.SetLocalTimeZone(TimeZoneInfo.Utc);
        _repository = new(_time);
    }

    [TestCleanup]
    public void RestoreCulture() => CultureInfo.CurrentCulture = _culture;

    [TestMethod]
    public async Task Free_ReadsOnlyPrimaryAndLast28Days()
    {
        // The old program's cross is inside the 28 days, so only "current program only" can keep it out.
        var old = await _repository.EnrollAsync("old", 1, "t", _today.AddDays(-10));
        await _repository.CrossSessionAsync(old.Id, 0, 0, _today.AddDays(-10));
        var current = await _repository.ReplaceEnrollmentAsync(old.Id, EnrollmentEndReason.Abandoned, "new", 1, "t", _today.AddDays(-3));
        await _repository.CrossSessionAsync(current.Id, 0, 0, _today.AddDays(-3));
        await _repository.LogBonusAsync(null, "Long ago", _today.AddDays(-28), 20);
        await _repository.LogBonusAsync(null, "Recent", _today.AddDays(-27), 20);

        var summary = await Create().GetAsync(fullHistory: false);

        summary.TotalWorkouts.Should().Be(2);
        summary.TotalMinutes.Should().Be(50);
        summary.Weeks.Should().HaveCount(StatsService.FreeWeeks);
        summary.Heatmap.Should().BeEmpty();
        summary.Programs.Should().BeEmpty();
    }

    [TestMethod]
    public async Task Pro_IncludesEndedPrograms()
    {
        var old = await _repository.EnrollAsync("old", 1, "t", _today.AddDays(-40));
        await _repository.CrossSessionAsync(old.Id, 0, 0, _today.AddDays(-40));
        await _repository.CrossSessionAsync(old.Id, 1, 0, _today.AddDays(-39));
        var current = await _repository.ReplaceEnrollmentAsync(old.Id, EnrollmentEndReason.Abandoned, "new", 1, "t", _today.AddDays(-3));
        await _repository.CrossSessionAsync(current.Id, 0, 0, _today.AddDays(-3));

        var summary = await Create().GetAsync(fullHistory: true);

        summary.TotalWorkouts.Should().Be(3);
        summary.Weeks.Should().HaveCount(StatsService.FullWeeks);
        summary.Heatmap.Should().HaveCount(StatsService.HeatmapWeeks * 7);
        summary.Programs.Select(p => (p.Name, p.CrossedDays)).Should().Equal(("Program new r1", 1), ("Program old r1", 2));
    }

    [TestMethod]
    public async Task EndedProgram_RateCountsOnlyUntilItEnded()
    {
        var old = await _repository.EnrollAsync("old", 1, "t", _today.AddDays(-2));
        await _repository.CrossSessionAsync(old.Id, 0, 0, _today.AddDays(-2));
        await _repository.EndEnrollmentAsync(old.Id, EnrollmentEndReason.Abandoned);
        _time.Advance(TimeSpan.FromDays(2));

        var summary = await Create().GetAsync(fullHistory: true);

        // Ended two days ago with three training days due and one crossed. The two days since don't count.
        var program = summary.Programs.Should().ContainSingle().Subject;
        (program.ScheduledDays, program.CrossedDays, program.CompletionRate).Should().Be((3, 1, 1d / 3));
    }

    [TestMethod]
    public async Task RestSessions_AreNotActivity()
    {
        var current = await _repository.EnrollAsync("new", 1, "t", _today.AddDays(-1));
        await _repository.CrossSessionAsync(current.Id, 0, 0, _today.AddDays(-1));
        await _repository.CrossSessionAsync(current.Id, 1, 0, _today);

        var summary = await Create().GetAsync(fullHistory: true);

        summary.TotalWorkouts.Should().Be(1);
        summary.ByKind.Should().Equal(new KindBucket(WorkoutKind.Cardio, 1, 30));
    }

    [TestMethod]
    public async Task Session_CountsOnItsCalendarDay_NotTheDayItWasCrossed()
    {
        // Wednesday's workout, crossed off on Saturday.
        var current = await _repository.EnrollAsync("old", 1, "t", _today.AddDays(-3));
        await _repository.CrossSessionAsync(current.Id, 0, 0, _today.AddDays(-3));

        var summary = await Create().GetAsync(fullHistory: true);

        summary.Heatmap.Single(d => d.Workouts > 0).Date.Should().Be(_today.AddDays(-3));
    }

    [TestMethod]
    public async Task Bonus_TakesKindAndLengthFromTheCatalog()
    {
        await _repository.LogBonusAsync("abs", "Abs", _today, null);
        await _repository.LogBonusAsync(null, "Walk", _today, null);
        await _repository.LogBonusAsync("run", "Run", _today, 45);

        var summary = await Create().GetAsync(fullHistory: true);

        summary.ByKind.Should().Equal(
            new KindBucket(WorkoutKind.Cardio, 1, 45),
            new KindBucket(WorkoutKind.Abs, 1, 15),
            new KindBucket(WorkoutKind.Mixed, 1, 0));
    }

    [TestMethod]
    public async Task NoData_IsAllZeros()
    {
        var summary = await Create().GetAsync(fullHistory: true);

        (summary.TotalWorkouts, summary.TotalMinutes, summary.ActiveDays).Should().Be((0, 0, 0));
        summary.Programs.Should().BeEmpty();
    }

    [TestMethod]
    public async Task Weeks_FollowTheCultureFirstDayOfWeek()
    {
        CultureInfo.CurrentCulture = new CultureInfo("en-US");

        var summary = await Create().GetAsync(fullHistory: true);

        summary.Weeks[^1].WeekStart.Should().Be(new DateOnly(2026, 11, 15));
    }

    private StatsService Create() =>
        new(TestEnrollments.Create(_repository, _catalog, new WeakReferenceMessenger(), new FakeSyncedSettings(), _entitlements), _catalog, _time);
}
