using AwesomeAssertions;
using ShowUp.Core.Catalog;
using ShowUp.Core.Stats;
using ShowUp.Core.Tracking;

namespace ShowUp.Core.Tests.Stats;

[TestClass]
public sealed class StatsCalculatorTests
{
    // A Saturday. The Monday-first week runs Nov 16 to Nov 22, the Sunday-first one Nov 15 to Nov 21.
    private static readonly DateOnly _today = new(2026, 11, 21);

    [TestMethod]
    public void Build_NoEntries_ZeroFilledBucketsAndZeroTotals()
    {
        var summary = Build([]);

        summary.TotalWorkouts.Should().Be(0);
        summary.TotalMinutes.Should().Be(0);
        summary.ActiveDays.Should().Be(0);
        summary.Weeks.Should().HaveCount(12).And.OnlyContain(w => w.Workouts == 0 && w.Minutes == 0);
        summary.Weeks[^1].WeekStart.Should().Be(new DateOnly(2026, 11, 16));
        summary.Weeks[0].WeekStart.Should().Be(new DateOnly(2026, 11, 16).AddDays(-77));
        summary.ByKind.Should().BeEmpty();
        summary.Heatmap.Should().OnlyContain(d => d.Workouts == 0);
    }

    [TestMethod]
    public void Build_OneEntry_OneNonEmptyBar()
    {
        var summary = Build([new(_today, 30, WorkoutKind.Cardio, IsBonus: false)]);

        summary.Weeks[^1].Should().Be(new WeekBucket(new DateOnly(2026, 11, 16), 1, 30));
        summary.Weeks.Take(11).Should().OnlyContain(w => w.Workouts == 0);
        (summary.TotalWorkouts, summary.TotalMinutes, summary.ActiveDays).Should().Be((1, 30, 1));
    }

    [TestMethod]
    public void Build_HeatmapIsAlways182Days()
    {
        var summary = Build([new(_today, 30, WorkoutKind.Cardio, false), new(_today, 15, WorkoutKind.Abs, true)]);

        summary.Heatmap.Should().HaveCount(182);
        summary.Heatmap[^1].Date.Should().Be(new DateOnly(2026, 11, 22));
        summary.Heatmap[0].Date.Should().Be(new DateOnly(2026, 11, 22).AddDays(-181));
        summary.Heatmap.Single(d => d.Date == _today).Workouts.Should().Be(2);
        summary.Heatmap.Select(d => d.Date).Should().BeInAscendingOrder();
    }

    [TestMethod]
    public void Build_SundayEntry_MondayFirst_CountsInEndingWeek()
    {
        ActivityEntry sunday = new(new DateOnly(2026, 11, 15), 30, WorkoutKind.Cardio, false);

        var summary = Build([sunday], firstDayOfWeek: DayOfWeek.Monday);

        summary.Weeks[^2].Should().Be(new WeekBucket(new DateOnly(2026, 11, 9), 1, 30));
        summary.Weeks[^1].Workouts.Should().Be(0);
    }

    [TestMethod]
    public void Build_SundayEntry_SundayFirst_CountsInStartingWeek()
    {
        ActivityEntry sunday = new(new DateOnly(2026, 11, 15), 30, WorkoutKind.Cardio, false);

        var summary = Build([sunday], firstDayOfWeek: DayOfWeek.Sunday);

        summary.Weeks[^1].Should().Be(new WeekBucket(new DateOnly(2026, 11, 15), 1, 30));
        summary.Heatmap[^1].Date.Should().Be(_today);
    }

    [TestMethod]
    public void Build_WeekAcrossNewYear_OneBucket()
    {
        DateOnly saturday = new(2027, 1, 2);
        ActivityEntry[] entries =
        [
            new(new DateOnly(2026, 12, 29), 30, WorkoutKind.Cardio, false),
            new(new DateOnly(2027, 1, 1), 20, WorkoutKind.Yoga, false),
        ];

        var summary = StatsCalculator.Build(entries, [], saturday, DayOfWeek.Monday, weeks: 12, heatmapWeeks: 26);

        summary.Weeks[^1].Should().Be(new WeekBucket(new DateOnly(2026, 12, 28), 2, 50));
    }

    [TestMethod]
    public void Build_ByKind_SortedByMinutes_EmptyKindsOmitted()
    {
        ActivityEntry[] entries =
        [
            new(_today, 30, WorkoutKind.Cardio, false),
            new(_today.AddDays(-1), 15, WorkoutKind.Abs, false),
            new(_today.AddDays(-2), 30, WorkoutKind.Cardio, false),
            new(_today.AddDays(-3), 50, WorkoutKind.Yoga, true),
        ];

        var summary = Build(entries);

        summary.ByKind.Should().Equal(
            new KindBucket(WorkoutKind.Cardio, 2, 60),
            new KindBucket(WorkoutKind.Yoga, 1, 50),
            new KindBucket(WorkoutKind.Abs, 1, 15));
    }

    [TestMethod]
    public void Build_BonusWithoutDuration_CountsWorkoutNotMinutes()
    {
        var summary = Build([new(_today, 0, WorkoutKind.Mixed, IsBonus: true)]);

        (summary.TotalWorkouts, summary.TotalMinutes).Should().Be((1, 0));
        summary.Weeks[^1].Should().Be(new WeekBucket(new DateOnly(2026, 11, 16), 1, 0));
    }

    [TestMethod]
    public void Build_EntryOlderThanTheBars_StillCountsInTotals()
    {
        var summary = Build([new(_today.AddYears(-1), 30, WorkoutKind.Cardio, false)]);

        summary.TotalWorkouts.Should().Be(1);
        summary.Weeks.Should().OnlyContain(w => w.Workouts == 0);
        summary.Heatmap.Should().OnlyContain(d => d.Workouts == 0);
    }

    [TestMethod]
    public void Build_TwoWorkoutsOnOneDay_OneActiveDay()
    {
        var summary = Build([new(_today, 30, WorkoutKind.Cardio, false), new(_today, 15, WorkoutKind.Abs, false)]);

        (summary.TotalWorkouts, summary.ActiveDays).Should().Be((2, 1));
    }

    [TestMethod]
    public void Build_NoHeatmapWeeks_NoHeatmap()
    {
        var summary = StatsCalculator.Build([new(_today, 30, WorkoutKind.Cardio, false)], [], _today, DayOfWeek.Monday, weeks: 4, heatmapWeeks: 0);

        summary.Weeks.Should().HaveCount(4);
        summary.Heatmap.Should().BeEmpty();
    }

    [TestMethod]
    public void Build_PassesProgramsThrough()
    {
        ProgramComparison program = new(Guid.NewGuid(), "P", 3, 4, 2, 0.75);

        StatsCalculator.Build([], [program], _today, DayOfWeek.Monday, 12, 26).Programs.Should().Equal(program);
    }

    [TestMethod]
    public void Compare_NoScheduledDays_RateIsZero()
    {
        DayStatus[] restOnly = [Day(0, rest: true), Day(1, rest: true)];

        var comparison = StatsCalculator.Compare(Guid.NewGuid(), "P", restOnly, _today, _today);

        (comparison.ScheduledDays, comparison.CrossedDays, comparison.CompletionRate).Should().Be((0, 0, 0d));
    }

    [TestMethod]
    public void Compare_CountsTrainingDaysThroughTheDate()
    {
        // Days start three days ago: four are due by today, two more lie ahead, one of the due ones is rest.
        DayStatus[] statuses = [Day(0, done: true), Day(1), Day(2, rest: true), Day(3, done: true), Day(4), Day(5)];
        var id = Guid.NewGuid();

        var comparison = StatsCalculator.Compare(id, "P", statuses, through: _today, _today);

        comparison.Should().Be(new ProgramComparison(id, "P", CrossedDays: 2, ScheduledDays: 3, LongestStreak: 1, CompletionRate: 2d / 3));
    }

    [TestMethod]
    public void Compare_EndedProgram_StopsCountingAtTheEnd()
    {
        DayStatus[] statuses = [Day(0, done: true), Day(1, done: true), Day(2), Day(3)];

        var comparison = StatsCalculator.Compare(Guid.NewGuid(), "P", statuses, through: _today.AddDays(-2), _today);

        (comparison.ScheduledDays, comparison.CrossedDays, comparison.CompletionRate).Should().Be((2, 2, 1d));
    }

    private static StatsSummary Build(ActivityEntry[] entries, DayOfWeek firstDayOfWeek = DayOfWeek.Monday) =>
        StatsCalculator.Build(entries, [], _today, firstDayOfWeek, weeks: 12, heatmapWeeks: 26);

    // Day 0 is three days before today.
    private static DayStatus Day(int index, bool done = false, bool rest = false) =>
        new(index, _today.AddDays(index - 3), 1, done ? new HashSet<int> { 0 } : new HashSet<int>(), IsRestDay: rest);
}
