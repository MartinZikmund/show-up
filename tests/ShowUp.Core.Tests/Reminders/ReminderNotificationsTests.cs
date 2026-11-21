using AwesomeAssertions;
using ShowUp.Core.Reminders;

namespace ShowUp.Core.Tests.Reminders;

[TestClass]
public sealed class ReminderNotificationsTests
{
    private static readonly Guid _id = Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e");
    private static readonly DateTimeOffset _now = new(2026, 11, 21, 10, 0, 0, TimeSpan.FromHours(1));

    private static PlannedReminder At(DateTimeOffset fireAt, Guid? id = null) => new(id ?? _id, fireAt, "Show Up", "Run is waiting. Show up.");

    [TestMethod]
    public void Key_SameReminderAndTime_IsStableAndFitsAWindowsToastTag()
    {
        var key = ReminderNotifications.Key(At(_now.AddHours(8)));

        key.Should().Be(ReminderNotifications.Key(At(_now.AddHours(8))));
        key.Should().StartWith(ReminderNotifications.KeyPrefix);
        key.Length.Should().BeLessThanOrEqualTo(64);
    }

    [TestMethod]
    public void Key_SameInstantInAnotherOffset_IsTheSame()
    {
        var fireAt = _now.AddHours(8);

        ReminderNotifications.Key(At(fireAt.ToOffset(TimeSpan.Zero))).Should().Be(ReminderNotifications.Key(At(fireAt)));
    }

    [TestMethod]
    public void Code_DifferentTimesOrReminders_Differ()
    {
        var codes = new[]
        {
            ReminderNotifications.Code(At(_now.AddHours(8))),
            ReminderNotifications.Code(At(_now.AddHours(32))),
            ReminderNotifications.Code(At(_now.AddHours(8), Guid.NewGuid())),
        };

        codes.Should().OnlyHaveUniqueItems();
        ReminderNotifications.Code(At(_now.AddHours(8))).Should().Be(codes[0]);
    }

    [TestMethod]
    public void Soonest_MoreThanTheCap_KeepsTheEarliest()
    {
        List<PlannedReminder> planned = [.. Enumerable.Range(0, 280).Select(i => At(_now.AddMinutes(280 - i)))];

        var kept = ReminderNotifications.Soonest(planned, 60);

        kept.Should().HaveCount(60);
        kept.Should().BeInAscendingOrder(r => r.FireAt);
        kept[0].FireAt.Should().Be(_now.AddMinutes(1));
        kept[^1].FireAt.Should().Be(_now.AddMinutes(60));
    }

    [TestMethod]
    public void Within_Window_KeepsOnlyTheFutureInsideIt()
    {
        List<PlannedReminder> planned = [At(_now.AddMinutes(-1)), At(_now.AddHours(1)), At(_now.AddHours(24)), At(_now.AddHours(25))];

        var kept = ReminderNotifications.Within(planned, _now, TimeSpan.FromHours(24));

        kept.Select(r => r.FireAt).Should().Equal(_now.AddHours(1), _now.AddHours(24));
    }

    [TestMethod]
    public void Rebase_NewZone_KeepsTheWallClockTime()
    {
        var prague = new DateTimeOffset(2026, 11, 22, 18, 0, 0, TimeSpan.FromHours(1));
        var tokyo = TimeZoneInfo.CreateCustomTimeZone("Test+9", TimeSpan.FromHours(9), "Test+9", "Test+9");

        var moved = ReminderNotifications.Rebase([At(prague)], _now, tokyo);

        moved.Should().ContainSingle().Which.FireAt.Should().Be(new DateTimeOffset(2026, 11, 22, 18, 0, 0, TimeSpan.FromHours(9)));
    }

    [TestMethod]
    public void Rebase_TimeAlreadyPassedInTheNewZone_DropsIt()
    {
        var tokyo = TimeZoneInfo.CreateCustomTimeZone("Test+9", TimeSpan.FromHours(9), "Test+9", "Test+9");

        // 10:30 in Tokyo is long gone at 09:00 UTC; 20:00 there is still ahead.
        var moved = ReminderNotifications.Rebase([At(_now.AddMinutes(30)), At(_now.AddHours(10))], _now, tokyo);

        moved.Select(r => r.FireAt).Should().Equal(new DateTimeOffset(2026, 11, 21, 20, 0, 0, TimeSpan.FromHours(9)));
    }

    [TestMethod]
    public void Serialize_RoundTrips()
    {
        List<PlannedReminder> planned = [At(_now.AddHours(8)), At(_now.AddHours(32), Guid.NewGuid())];

        var back = ReminderNotifications.Deserialize(ReminderNotifications.Serialize(planned));

        back.Should().Equal(planned);
        back[0].FireAt.Offset.Should().Be(TimeSpan.FromHours(1));
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("{not json")]
    public void Deserialize_MissingOrBroken_IsEmpty(string? json) =>
        ReminderNotifications.Deserialize(json).Should().BeEmpty();
}
