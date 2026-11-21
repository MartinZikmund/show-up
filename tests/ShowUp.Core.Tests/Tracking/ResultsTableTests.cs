using AwesomeAssertions;
using ShowUp.Core.Catalog;
using ShowUp.Core.Storage;
using ShowUp.Core.Tracking;

namespace ShowUp.Core.Tests.Tracking;

[TestClass]
public sealed class ResultsTableTests
{
    private static readonly DateOnly _start = new(2026, 11, 1);
    private static readonly Guid _enrollment = Guid.NewGuid();

    private static readonly Workout _fitTest = new(
        "fit",
        "Fit Test",
        30,
        WorkoutKind.FitTest,
        null,
        [new("jacks", "Power Jacks", MetricUnit.Reps), new("knees", "Power Knees", MetricUnit.Reps)]);

    private static readonly Workout _sprint = new("sprint", "Sprint", 10, WorkoutKind.Cardio, null, [new("time", "Time", MetricUnit.Duration, HigherIsBetter: false)]);

    private static readonly Dictionary<string, Workout> _workouts = new()
    {
        ["fit"] = _fitTest,
        ["sprint"] = _sprint,
        ["run"] = new("run", "Run", 30, WorkoutKind.Cardio, null),
    };

    [TestMethod]
    public void ResultsTable_FitTests_OrderedByDayWithDeltas()
    {
        ScheduleDay[] days = [Day("fit"), Day("run"), Day("run"), Day("fit"), Day("fit")];

        var tables = Build(days, [Entry(3, "jacks", 50), Entry(0, "jacks", 40), Entry(0, "knees", 30), Entry(3, "knees", 25), Entry(4, "jacks", 40)]);

        var table = tables.Should().ContainSingle().Subject;
        table.Workout.Should().Be(_fitTest);
        table.Columns.Select(c => c.DayIndex).Should().Equal(0, 3, 4);
        table.Columns.Select(c => c.Date).Should().Equal(_start, _start.AddDays(3), _start.AddDays(4));
        table.Rows.Select(r => r.Metric.Id).Should().Equal("jacks", "knees");

        var jacks = table.Rows[0].Cells;
        jacks.Select(c => c.Value?.Number).Should().Equal(40, 50, 40);
        jacks.Select(c => c.Delta).Should().Equal(null, 10, 0);
        jacks.Select(c => c.Improved).Should().Equal(null, true, null);

        var knees = table.Rows[1].Cells;
        knees[1].Delta.Should().Be(-5);
        knees[1].Improved.Should().BeFalse();
    }

    [TestMethod]
    public void ResultsTable_MissingValues_AreEmpty()
    {
        ScheduleDay[] days = [Day("fit"), Day("fit"), Day("fit")];

        var table = Build(days, [Entry(1, "jacks", 44)]).Single();

        table.Rows[0].Cells.Select(c => c.IsEmpty).Should().Equal(true, false, true);
        table.Rows[0].Cells[1].Delta.Should().BeNull("the first logged value is the baseline");
        table.Rows[1].Cells.Should().OnlyContain(c => c.IsEmpty && c.Delta == null);
        table.HasValues.Should().BeTrue();
    }

    [TestMethod]
    public void ResultsTable_NoEntries_StillListsScheduledOccurrences()
    {
        var table = Build([Day("fit"), Day("run"), Day("fit")], []).Single();

        table.Columns.Should().HaveCount(2);
        table.HasValues.Should().BeFalse();
    }

    [TestMethod]
    public void ResultsTable_LowerIsBetter_ShorterTimeImproves()
    {
        var table = Build([Day("sprint"), Day("sprint")], [Entry(0, "time", 90, "sprint"), Entry(1, "time", 80, "sprint")]).Single();

        table.Rows[0].Cells[1].Delta.Should().Be(-10);
        table.Rows[0].Cells[1].Improved.Should().BeTrue();
    }

    [TestMethod]
    public void ResultsTable_LoggedBonus_JoinsTheWorkoutsTableByDate()
    {
        BonusWorkoutEntity logged = new() { Id = Guid.NewGuid(), WorkoutId = "fit", Title = "Fit Test", Date = _start.AddDays(1) };
        BonusWorkoutEntity unlogged = new() { Id = Guid.NewGuid(), WorkoutId = "fit", Title = "Fit Test", Date = _start.AddDays(2) };
        BonusWorkoutEntity otherWorkout = new() { Id = Guid.NewGuid(), WorkoutId = "run", Title = "Run", Date = _start };

        var tables = ResultsTable.Build(
            [Day("fit"), Day("run"), Day("run"), Day("fit")],
            i => _start.AddDays(i),
            _workouts,
            [Entry(0, "jacks", 40)],
            [logged, unlogged, otherWorkout],
            [new MetricEntryEntity { BonusWorkoutId = logged.Id, WorkoutId = "fit", MetricId = "jacks", Value = 45 }]);

        var table = tables.Should().ContainSingle().Subject;
        table.Columns.Select(c => (c.DayIndex, c.BonusWorkoutId)).Should().Equal((0, null), (null, logged.Id), (3, null));
        table.Rows[0].Cells[1].Delta.Should().Be(5);
    }

    [TestMethod]
    public void ResultsTable_BonusOnlyWorkout_GetsItsOwnTable()
    {
        BonusWorkoutEntity bonus = new() { Id = Guid.NewGuid(), WorkoutId = "sprint", Title = "Sprint", Date = _start };

        var tables = ResultsTable.Build(
            [Day("fit")],
            i => _start.AddDays(i),
            _workouts,
            [],
            [bonus],
            [new MetricEntryEntity { BonusWorkoutId = bonus.Id, WorkoutId = "sprint", MetricId = "time", Value = 75 }]);

        tables.Select(t => t.Workout.Id).Should().Equal("fit", "sprint");
    }

    [TestMethod]
    public void UpToNext_FutureOccurrences_KeepsOnlyTheNextOne()
    {
        var table = Build([Day("fit"), Day("fit"), Day("fit"), Day("fit")], [Entry(0, "jacks", 40)]).Single();

        var trimmed = table.UpToNext(_start.AddDays(1));

        trimmed.Columns.Select(c => c.DayIndex).Should().Equal(0, 1, 2);
        trimmed.Rows.Should().OnlyContain(r => r.Cells.Count == 3);
        trimmed.Rows[0].Cells[0].Value!.Value.Number.Should().Be(40);
    }

    [TestMethod]
    public void NextScheduled_TodayOrLater_SkipsThePast()
    {
        var table = Build([Day("fit"), Day("run"), Day("fit"), Day("fit")], []).Single();

        table.NextScheduled(_start.AddDays(1))!.DayIndex.Should().Be(2);
        table.NextScheduled(_start.AddDays(2))!.DayIndex.Should().Be(2);
        table.NextScheduled(_start.AddDays(4)).Should().BeNull();
    }

    private static IReadOnlyList<ResultsTable> Build(IReadOnlyList<ScheduleDay> days, IEnumerable<MetricEntryEntity> entries) =>
        ResultsTable.Build(days, i => _start.AddDays(i), _workouts, entries, [], []);

    private static ScheduleDay Day(string workoutId) => new([new ScheduledSession(workoutId)]);

    private static MetricEntryEntity Entry(int day, string metric, double value, string workout = "fit") => new()
    {
        EnrollmentId = _enrollment,
        DayIndex = day,
        SessionIndex = 0,
        WorkoutId = workout,
        MetricId = metric,
        Value = value,
    };
}
