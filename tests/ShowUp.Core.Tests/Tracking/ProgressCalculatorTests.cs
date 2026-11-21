using AwesomeAssertions;
using ShowUp.Core.Catalog;
using ShowUp.Core.Tracking;

namespace ShowUp.Core.Tests.Tracking;

[TestClass]
public class ProgressCalculatorTests
{
    private static readonly DateOnly _start = new(2026, 12, 1);

    private static readonly IReadOnlyDictionary<string, Workout> _workouts = new Dictionary<string, Workout>
    {
        ["run"] = new("run", "Run", 30, WorkoutKind.Cardio, null),
        ["rest"] = new("rest", "Rest", null, WorkoutKind.Rest, null),
    };

    private static ProgramTrack Track(params string[][] days) =>
        new(
            "t",
            "Track",
            [new ProgramPhase("P", [new ScheduleWeek([.. days.Select(d => new ScheduleDay([.. d.Select(w => new ScheduledSession(w))]))])])]);

    private static IReadOnlyList<DayStatus> Build(ProgramTrack track, params (int Day, int Session)[] completed) =>
        ProgressCalculator.Build(
            track,
            _workouts,
            new EnrollmentCalendar(_start, track.Days.Count, []),
            completed.ToHashSet());

    [TestMethod]
    public void Build_TrackDays_ReturnsOneStatusPerDayWithDates()
    {
        IReadOnlyList<DayStatus> result = Build(Track(["run"], ["rest"], ["run", "run"]), (0, 0));

        result.Should().HaveCount(3);
        result[2].Date.Should().Be(_start.AddDays(2));
        result[2].SessionCount.Should().Be(2);
        result[1].IsRestDay.Should().BeTrue();
        result[0].Progress.Should().Be(DayProgress.Complete);
    }

    [TestMethod]
    public void Build_CompletedOutOfRangeSession_IsIgnored()
    {
        IReadOnlyList<DayStatus> result = Build(Track(["run"]), (0, 5), (9, 0));

        result[0].CompletedSessions.Should().BeEmpty();
        result[0].Progress.Should().Be(DayProgress.Empty);
    }

    [TestMethod]
    public void Build_CalendarDayCountMismatch_Throws()
    {
        ProgramTrack track = Track(["run"], ["run"]);
        EnrollmentCalendar calendar = new(_start, 5, []);

        Action act = () => ProgressCalculator.Build(track, _workouts, calendar, new HashSet<(int Day, int Session)>());

        act.Should().Throw<ArgumentException>();
    }
}
