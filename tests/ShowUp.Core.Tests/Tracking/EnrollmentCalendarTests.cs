using AwesomeAssertions;
using ShowUp.Core.Tracking;

namespace ShowUp.Core.Tests.Tracking;

[TestClass]
public class EnrollmentCalendarTests
{
    private static readonly DateOnly _start = new(2026, 12, 1);

    [TestMethod]
    public void DayCount_MatchesConstructorArgument()
    {
        EnrollmentCalendar calendar = new(_start, 42, [new ScheduleShift(_start.AddDays(3), 2)]);

        calendar.DayCount.Should().Be(42);
    }

    [TestMethod]
    public void DateOf_NoShifts_IsStartPlusIndex()
    {
        EnrollmentCalendar calendar = new(_start, 90, []);

        calendar.DateOf(9).Should().Be(new DateOnly(2026, 12, 10));
    }

    [TestMethod]
    public void DateOf_ShiftFromDay3By2_MovesLaterDaysOnly()
    {
        EnrollmentCalendar calendar = new(_start, 90, [new ScheduleShift(new DateOnly(2026, 12, 4), 2)]);

        calendar.DateOf(1).Should().Be(new DateOnly(2026, 12, 2));
        calendar.DateOf(3).Should().Be(new DateOnly(2026, 12, 6));
    }

    [TestMethod]
    public void DateOf_TwoShifts_Accumulate()
    {
        EnrollmentCalendar calendar = new(
            _start,
            90,
            [new ScheduleShift(new DateOnly(2026, 12, 4), 2), new ScheduleShift(new DateOnly(2026, 12, 10), 1)]);

        calendar.DateOf(8).Should().Be(new DateOnly(2026, 12, 12));
    }

    [TestMethod]
    public void DayIndexOn_GapDay_ReturnsNull()
    {
        EnrollmentCalendar calendar = new(_start, 90, [new ScheduleShift(new DateOnly(2026, 12, 4), 2)]);

        calendar.DayIndexOn(new DateOnly(2026, 12, 4)).Should().BeNull();
        calendar.DayIndexOn(new DateOnly(2026, 12, 6)).Should().Be(3);
    }

    [TestMethod]
    public void DayIndexOn_OutsideRange_ReturnsNull()
    {
        EnrollmentCalendar calendar = new(_start, 10, []);

        calendar.DayIndexOn(_start.AddDays(-1)).Should().BeNull();
        calendar.DayIndexOn(_start.AddDays(10)).Should().BeNull();
        calendar.DayIndexOn(_start.AddDays(9)).Should().Be(9);
    }

    [TestMethod]
    public void EndDate_WithShift_IncludesShift()
    {
        EnrollmentCalendar calendar = new(_start, 90, [new ScheduleShift(new DateOnly(2026, 12, 4), 3)]);

        calendar.EndDate.Should().Be(_start.AddDays(92));
    }

    [TestMethod]
    public void Constructor_InvalidDayCount_Throws()
    {
        Action act = () => _ = new EnrollmentCalendar(_start, 0, []);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [TestMethod]
    public void Constructor_ShiftWithZeroDays_Throws()
    {
        Action act = () => _ = new EnrollmentCalendar(_start, 10, [new ScheduleShift(_start, 0)]);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [TestMethod]
    public void DateOf_IndexOutOfRange_Throws()
    {
        EnrollmentCalendar calendar = new(_start, 10, []);

        Action act = () => calendar.DateOf(10);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
