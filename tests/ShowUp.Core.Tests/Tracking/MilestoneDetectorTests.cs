using AwesomeAssertions;
using ShowUp.Core.Catalog;
using ShowUp.Core.Tracking;

namespace ShowUp.Core.Tests.Tracking;

[TestClass]
public sealed class MilestoneDetectorTests
{
    private static readonly DateOnly _start = new(2026, 11, 2);

    // Two weeks of two days each, one phase.
    private static readonly ProgramTrack _onePhase = Track(("Phase", [["run", "run"], ["run", "run"]]));

    // Month 1 has two weeks, Month 2 has one.
    private static readonly ProgramTrack _twoPhases = Track(
        ("Month 1", [["run", "run"], ["run", "run"]]),
        ("Month 2", [["run", "run"]]));

    [TestMethod]
    public void Detect_LastSessionOfWeek_ReturnsWeek()
    {
        var milestone = Detect(_onePhase, before: [(0, 0)], crossed: (1, 0));

        milestone.Should().Be(new Milestone(MilestoneKind.Week, "Phase", 1));
    }

    [TestMethod]
    public void Detect_CrossIntoAWeekThatStaysOpen_ReturnsNull()
    {
        // Week 1 is complete already and week 2 still has a day left: this cross finishes nothing.
        var milestone = Detect(_onePhase, before: [(0, 0), (1, 0)], crossed: (2, 0));

        milestone.Should().BeNull();
    }

    [TestMethod]
    public void Detect_LastWeekOfPhase_ReturnsPhase()
    {
        var milestone = Detect(_twoPhases, before: [(0, 0), (1, 0), (2, 0)], crossed: (3, 0));

        milestone.Should().Be(new Milestone(MilestoneKind.Phase, "Month 1", 2));
    }

    [TestMethod]
    public void Detect_SinglePhaseProgram_NeverReturnsPhase()
    {
        ProgramTrack track = Track(("Phase", [["run", "run"]]), ("Phase 2", [["run"]]));
        ProgramTrack single = Track(("Phase", [["run", "run"], ["run"]]));

        Detect(track, before: [(0, 0)], crossed: (1, 0))!.Kind.Should().Be(MilestoneKind.Phase);
        Detect(single, before: [(0, 0)], crossed: (1, 0))!.Kind.Should().Be(MilestoneKind.Week);
    }

    [TestMethod]
    public void Detect_LastDayOfProgram_ReturnsProgram()
    {
        var milestone = Detect(_twoPhases, before: [(0, 0), (1, 0), (2, 0), (3, 0), (4, 0)], crossed: (5, 0));

        milestone.Should().Be(new Milestone(MilestoneKind.Program, "Month 2", 3));
    }

    [TestMethod]
    public void Detect_LastDayCrossedWithEarlierDayEmpty_ReturnsWeekOnly()
    {
        // Day 0 was skipped: the last week is done, the program is not.
        var milestone = Detect(_onePhase, before: [(1, 0), (2, 0)], crossed: (3, 0));

        milestone.Should().Be(new Milestone(MilestoneKind.Week, "Phase", 2));
    }

    [TestMethod]
    public void Detect_OnlyOptionalAndRestLeft_WeekIsComplete()
    {
        ProgramTrack track = Track(("Phase", [["run+abs?", "rest", "run"], ["run"]]));

        var milestone = Detect(track, before: [(0, 0)], crossed: (2, 0));

        milestone.Should().Be(new Milestone(MilestoneKind.Week, "Phase", 1));
    }

    [TestMethod]
    public void Detect_TwoSessionDayFirstSession_ReturnsNull()
    {
        ProgramTrack track = Track(("Phase", [["run+abs"]]));

        Detect(track, before: [], crossed: (0, 0)).Should().BeNull();
    }

    [TestMethod]
    public void Detect_OptionalSessionOnACompleteWeek_ReturnsNull()
    {
        ProgramTrack track = Track(("Phase", [["run+abs?"], ["run"]]));

        Detect(track, before: [(0, 0)], crossed: (0, 1)).Should().BeNull();
    }

    [TestMethod]
    public void IsProgramComplete_EveryRequiredSessionCrossed_IsTrue()
    {
        ProgramTrack track = Track(("Phase", [["run+abs?", "rest", "run"]]));

        MilestoneDetector.IsProgramComplete(Statuses(track, [(0, 0), (2, 0)])).Should().BeTrue();
        MilestoneDetector.IsProgramComplete(Statuses(track, [(0, 0)])).Should().BeFalse();
    }

    [TestMethod]
    public void IsProgramComplete_NothingToCross_IsFalse()
    {
        ProgramTrack restOnly = Track(("Phase", [["rest", "rest"]]));

        MilestoneDetector.IsProgramComplete(Statuses(restOnly, [])).Should().BeFalse();
        MilestoneDetector.IsProgramComplete([]).Should().BeFalse();
    }

    private static Milestone? Detect(ProgramTrack track, (int Day, int Session)[] before, (int Day, int Session) crossed) =>
        MilestoneDetector.Detect(track, Statuses(track, before), Statuses(track, [.. before, crossed]), crossed.Day);

    /// <summary>Each phase is a name and its weeks; a day is "run", "rest", "run+abs" or "run+abs?" (optional).</summary>
    private static ProgramTrack Track(params (string Name, string[][] Weeks)[] phases) =>
        new("t", "Track",
        [
            .. phases.Select(p => new ProgramPhase(p.Name,
            [
                .. p.Weeks.Select(week => new ScheduleWeek(
                [
                    .. week.Select(day => new ScheduleDay(
                        [.. day.Split('+').Select(w => new ScheduledSession(w.TrimEnd('?'), Optional: w.EndsWith('?')))])),
                ])),
            ])),
        ]);

    private static IReadOnlyList<DayStatus> Statuses(ProgramTrack track, (int Day, int Session)[] crossed)
    {
        var done = crossed.ToLookup(c => c.Day, c => c.Session);
        return
        [
            .. track.Days.Select((day, index) => new DayStatus(
                index,
                _start.AddDays(index),
                day.Sessions.Count,
                done[index].ToHashSet(),
                IsRestDay: day.Sessions.All(s => s.WorkoutId == "rest"),
                day.Sessions.Select((session, n) => (session, n)).Where(x => x.session.Optional).Select(x => x.n).ToHashSet())),
        ];
    }
}
