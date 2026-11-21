using AwesomeAssertions;
using ShowUp.Core.Reminders;
using ShowUp.Core.Storage;

namespace ShowUp.Core.Tests.Reminders;

[TestClass]
public sealed class ReminderPlannerTests
{
    private const int EveryDay = 127;

    // Prague rules: +1, and +2 from the last Sunday of March 02:00 to the last Sunday of October 03:00.
    private static readonly TimeZoneInfo _prague = TimeZoneInfo.CreateCustomTimeZone(
        "Test/Prague",
        TimeSpan.FromHours(1),
        "Test Prague",
        "CET",
        "CEST",
        [
            TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(
                DateTime.MinValue.Date,
                DateTime.MaxValue.Date,
                TimeSpan.FromHours(1),
                TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 2, 0, 0), 3, 5, DayOfWeek.Sunday),
                TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 3, 0, 0), 10, 5, DayOfWeek.Sunday)),
        ]);

    private static readonly Func<DateOnly, ReminderDay> _workout = _ => new ReminderDay(false, "Plyometric Cardio Circuit", false);

    private static ReminderEntity Reminder(int hour, int minute = 0, int mask = EveryDay, bool enabled = true) =>
        new() { Id = Guid.NewGuid(), MinutesOfDay = hour * 60 + minute, DaysMask = mask, Enabled = enabled };

    private static DateTimeOffset Prague(int y, int m, int d, int h, int min = 0) =>
        new(new DateTime(y, m, d, h, min, 0), _prague.GetUtcOffset(new DateTime(y, m, d, h, min, 0)));

    [TestMethod]
    public void Plan_TimePassedToday_StartsTomorrow()
    {
        var plan = ReminderPlanner.Plan([Reminder(8)], Prague(2026, 11, 21, 10), _prague, 14, _workout);

        plan.Should().HaveCount(13);
        plan[0].FireAt.Should().Be(Prague(2026, 11, 22, 8));
    }

    [TestMethod]
    public void Plan_TimeStillAheadToday_IncludesToday()
    {
        var plan = ReminderPlanner.Plan([Reminder(18)], Prague(2026, 11, 21, 10), _prague, 14, _workout);

        plan[0].FireAt.Should().Be(Prague(2026, 11, 21, 18));
        plan.Should().HaveCount(14);
    }

    [TestMethod]
    public void Plan_DstSpringForward_KeepsWallClock()
    {
        var plan = ReminderPlanner.Plan([Reminder(7, 30)], Prague(2026, 3, 27, 12), _prague, 4, _workout);

        plan.Select(p => (p.FireAt.Hour, p.FireAt.Minute)).Should().AllBeEquivalentTo((7, 30));
        plan.Select(p => p.FireAt.Offset.TotalHours).Should().Equal(1, 2, 2);
    }

    [TestMethod]
    public void Plan_SkippedSpringForwardTime_MovesToNextValidMinute()
    {
        var plan = ReminderPlanner.Plan([Reminder(2, 30)], Prague(2026, 3, 28, 12), _prague, 3, _workout);

        plan[0].FireAt.Should().Be(new DateTimeOffset(2026, 3, 29, 3, 0, 0, TimeSpan.FromHours(2)));
        plan[1].FireAt.Should().Be(Prague(2026, 3, 30, 2, 30));
    }

    [TestMethod]
    public void Plan_DstFallBack_KeepsWallClockAndTakesFirstOccurrenceWhenRepeated()
    {
        var plan = ReminderPlanner.Plan([Reminder(2, 30), Reminder(8)], Prague(2026, 10, 24, 12), _prague, 3, _workout);

        var repeated = plan.Single(p => p.FireAt.Day == 25 && p.FireAt.Hour == 2);
        repeated.FireAt.Offset.Should().Be(TimeSpan.FromHours(2), "02:30 happens twice and the first one counts");
        plan.Single(p => p.FireAt.Day == 26 && p.FireAt.Hour == 8).FireAt.Offset.Should().Be(TimeSpan.FromHours(1));
    }

    [TestMethod]
    public void Plan_DayCompleted_Skips()
    {
        var done = new DateOnly(2026, 11, 23);

        var plan = ReminderPlanner.Plan([Reminder(8)], Prague(2026, 11, 21, 10), _prague, 5, d => new ReminderDay(d == done, "Run", false));

        plan.Select(p => p.FireAt.Day).Should().Equal(22, 24, 25);
    }

    [TestMethod]
    public void Plan_RestDay_RestMessage()
    {
        var plan = ReminderPlanner.Plan([Reminder(8)], Prague(2026, 11, 21, 10), _prague, 2, _ => new ReminderDay(false, null, true));

        plan.Should().ContainSingle().Which.Body.Should().Be("Rest day. You earned it.");
    }

    [TestMethod]
    public void Plan_WorkoutDay_NamesTheWorkout()
    {
        var plan = ReminderPlanner.Plan([Reminder(8)], Prague(2026, 11, 21, 10), _prague, 2, _workout);

        plan[0].Body.Should().Be("Plyometric Cardio Circuit is waiting. Show up.");
        plan[0].Title.Should().Be("Show Up");
    }

    [TestMethod]
    public void Plan_WeekdayMask_OnlyThoseDays()
    {
        // Bit 0 = Monday, bit 6 = Sunday: Monday and Sunday.
        var plan = ReminderPlanner.Plan([Reminder(8, mask: 0b1000001)], Prague(2026, 11, 21, 10), _prague, 14, _workout);

        plan.Select(p => p.FireAt.DayOfWeek).Should().OnlyContain(d => d == DayOfWeek.Monday || d == DayOfWeek.Sunday);
        plan.Should().HaveCount(4);
    }

    [TestMethod]
    public void Plan_DisabledReminder_IsIgnored()
    {
        var plan = ReminderPlanner.Plan([Reminder(8, enabled: false)], Prague(2026, 11, 21, 10), _prague, 14, _workout);

        plan.Should().BeEmpty();
    }

    [TestMethod]
    public void Plan_SeveralReminders_SoonestFirst()
    {
        var plan = ReminderPlanner.Plan([Reminder(20), Reminder(7)], Prague(2026, 11, 21, 6), _prague, 1, _workout);

        plan.Select(p => p.FireAt.Hour).Should().Equal(7, 20);
    }

    [TestMethod]
    public void Plan_NowInAnotherOffset_UsesTheZonesToday()
    {
        // 23:30 UTC on the 20th is already the 21st in Prague.
        var now = new DateTimeOffset(2026, 11, 20, 23, 30, 0, TimeSpan.Zero);

        var plan = ReminderPlanner.Plan([Reminder(8)], now, _prague, 1, _workout);

        plan.Should().ContainSingle().Which.FireAt.Should().Be(Prague(2026, 11, 21, 8));
    }
}
