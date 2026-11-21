using AwesomeAssertions;
using ShowUp.Core.Tracking;

namespace ShowUp.Core.Tests.Tracking;

[TestClass]
public class DayStatusTests
{
    private static readonly DateOnly _date = new(2026, 12, 1);

    private static DayStatus Status(int sessions, int done, bool rest = false) =>
        new(0, _date, sessions, Enumerable.Range(0, done).ToHashSet(), rest);

    [TestMethod]
    public void MarkStrokes_SingleSessionDone_IsTwo()
    {
        DayStatus status = Status(1, 1);

        status.MarkStrokes.Should().Be(2);
        status.Progress.Should().Be(DayProgress.Complete);
    }

    [TestMethod]
    public void MarkStrokes_TwoSessionsOneDone_IsOne()
    {
        DayStatus status = Status(2, 1);

        status.MarkStrokes.Should().Be(1);
        status.Progress.Should().Be(DayProgress.Partial);
    }

    [TestMethod]
    public void MarkStrokes_ThreeSessionsTwoDone_IsZeroWithTicks()
    {
        DayStatus status = Status(3, 2);

        status.MarkStrokes.Should().Be(0);
        status.ShowsSessionTicks.Should().BeTrue();
    }

    [TestMethod]
    public void MarkStrokes_NothingDone_IsZeroAndEmpty()
    {
        DayStatus status = Status(2, 0);

        status.MarkStrokes.Should().Be(0);
        status.Progress.Should().Be(DayProgress.Empty);
    }

    [TestMethod]
    public void MarkStrokes_RestDayWithNoSessions_IsZeroAndEmpty()
    {
        DayStatus status = Status(0, 0, rest: true);

        status.MarkStrokes.Should().Be(0);
        status.Progress.Should().Be(DayProgress.Empty);
    }

    [TestMethod]
    public void IsMissed_PastUncrossedRestDay_IsFalse()
    {
        DayStatus status = Status(1, 0, rest: true);

        status.IsMissed(_date.AddDays(3)).Should().BeFalse();
    }

    [TestMethod]
    public void IsMissed_PastIncompleteTrainingDay_IsTrue()
    {
        Status(1, 0).IsMissed(_date.AddDays(1)).Should().BeTrue();
    }

    [TestMethod]
    public void IsMissed_TodayIncomplete_IsFalse()
    {
        Status(1, 0).IsMissed(_date).Should().BeFalse();
    }

    [TestMethod]
    public void IsMissed_PastCompleteDay_IsFalse()
    {
        Status(1, 1).IsMissed(_date.AddDays(1)).Should().BeFalse();
    }
}
