using AwesomeAssertions;
using ShowUp.Core.Tracking;

namespace ShowUp.Core.Tests.Tracking;

[TestClass]
public class StreakCalculatorTests
{
    private static readonly DateOnly _start = new(2026, 12, 1);

    // 'x' done, '.' pending training, 'r' rest
    private static IReadOnlyList<DayStatus> Days(string pattern) =>
        [.. pattern.Select((c, i) => new DayStatus(
            i,
            _start.AddDays(i),
            c == 'r' ? 0 : 1,
            c == 'x' ? new HashSet<int> { 0 } : new HashSet<int>(),
            c == 'r'))];

    private static DateOnly Day(int i) => _start.AddDays(i);

    [TestMethod]
    public void Current_TodayPending_DoesNotBreak()
    {
        StreakCalculator.Current(Days("xx."), Day(2)).Should().Be(2);
    }

    [TestMethod]
    public void Current_TodayDone_Counts()
    {
        StreakCalculator.Current(Days("xxx"), Day(2)).Should().Be(3);
    }

    [TestMethod]
    public void Current_UncrossedRestDayBetween_KeepsCounting()
    {
        StreakCalculator.Current(Days("xrx"), Day(2)).Should().Be(2);
    }

    [TestMethod]
    public void Current_MissedTrainingDay_Breaks()
    {
        StreakCalculator.Current(Days("xx.x"), Day(3)).Should().Be(1);
    }

    [TestMethod]
    public void Current_FutureDays_AreIgnored()
    {
        StreakCalculator.Current(Days("xxxx"), Day(1)).Should().Be(2);
    }

    [TestMethod]
    public void Longest_TwoRuns_ReturnsLonger()
    {
        StreakCalculator.Longest(Days("xx.xxxrx"), Day(7)).Should().Be(4);
    }

    [TestMethod]
    public void Longest_NoneDone_IsZero()
    {
        StreakCalculator.Longest(Days("..."), Day(2)).Should().Be(0);
    }

    [TestMethod]
    public void Current_OptionalSessionSkipped_StillCounts()
    {
        DayStatus[] days =
        [
            new(0, _start, 2, new HashSet<int> { 0 }, false, new HashSet<int> { 1 }),
            new(1, _start.AddDays(1), 2, new HashSet<int> { 0 }, false, new HashSet<int> { 1 }),
        ];

        StreakCalculator.Current(days, Day(1)).Should().Be(2);
    }

    [TestMethod]
    public void Current_AllOptionalDayUndone_ActsLikeRest()
    {
        DayStatus[] days =
        [
            new(0, _start, 1, new HashSet<int> { 0 }, false),
            new(1, _start.AddDays(1), 1, new HashSet<int>(), false, new HashSet<int> { 0 }),
            new(2, _start.AddDays(2), 1, new HashSet<int> { 0 }, false),
        ];

        StreakCalculator.Current(days, Day(2)).Should().Be(2);
        StreakCalculator.Longest(days, Day(2)).Should().Be(2);
    }
}
