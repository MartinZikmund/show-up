using AwesomeAssertions;
using ShowUp.Core.Tracking;

namespace ShowUp.Core.Tests.Tracking;

[TestClass]
public sealed class CalendarFocusMapTests
{
    // Three full weeks and a short last one.
    private static readonly int[] _weeks = [7, 7, 7, 3];

    [TestMethod]
    public void Right_WithinARow_GoesToTheNextDay() =>
        Move(1, 2, CalendarFocusMove.Right).Should().Be((1, 3));

    [TestMethod]
    public void Right_FromLastOfRow_GoesToNextRow() =>
        Move(0, 6, CalendarFocusMove.Right).Should().Be((1, 0));

    [TestMethod]
    public void Right_FromTheLastDay_ReturnsNull() =>
        Move(3, 2, CalendarFocusMove.Right).Should().BeNull();

    [TestMethod]
    public void Left_FromFirstOfRow_GoesToEndOfPreviousRow() =>
        Move(2, 0, CalendarFocusMove.Left).Should().Be((1, 6));

    [TestMethod]
    public void Left_FromFirst_ReturnsNull() =>
        Move(0, 0, CalendarFocusMove.Left).Should().BeNull();

    [TestMethod]
    public void Up_KeepsTheColumn() =>
        Move(2, 4, CalendarFocusMove.Up).Should().Be((1, 4));

    [TestMethod]
    public void Up_FromFirstRow_ReturnsNull() =>
        Move(0, 4, CalendarFocusMove.Up).Should().BeNull();

    [TestMethod]
    public void Down_FromLastRow_ReturnsNull() =>
        Move(3, 1, CalendarFocusMove.Down).Should().BeNull();

    [TestMethod]
    public void Down_IntoShortLastRow_ClampsToLastCell() =>
        Move(2, 5, CalendarFocusMove.Down).Should().Be((3, 2));

    [TestMethod]
    public void Home_GoesToRowStart() =>
        Move(1, 4, CalendarFocusMove.Home).Should().Be((1, 0));

    [TestMethod]
    public void End_GoesToRowEnd_OfAShortRowToo()
    {
        Move(1, 2, CalendarFocusMove.End).Should().Be((1, 6));
        Move(3, 0, CalendarFocusMove.End).Should().Be((3, 2));
    }

    [TestMethod]
    public void Move_SkipsEmptyWeeks() =>
        CalendarFocusMap.Move(0, 6, CalendarFocusMove.Right, [7, 0, 7]).Should().Be((2, 0));

    [TestMethod]
    [DataRow(-1, 0)]
    [DataRow(4, 0)]
    [DataRow(3, 3)]
    [DataRow(0, -1)]
    public void Move_FromOutsideTheGrid_ReturnsNull(int week, int day) =>
        Move(week, day, CalendarFocusMove.Right).Should().BeNull();

    private static (int Week, int Day)? Move(int week, int day, CalendarFocusMove move) =>
        CalendarFocusMap.Move(week, day, move, _weeks);
}
