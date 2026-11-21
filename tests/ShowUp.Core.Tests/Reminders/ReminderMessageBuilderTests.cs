using AwesomeAssertions;
using ShowUp.Core.Reminders;

namespace ShowUp.Core.Tests.Reminders;

[TestClass]
public sealed class ReminderMessageBuilderTests
{
    private readonly ReminderMessageBuilder _messages = EnglishReminderLocalizer.Messages;

    [TestMethod]
    public void Body_Workout_NamesIt()
    {
        _messages.Body(new ReminderDay(false, "Plyometric Cardio Circuit", false)).Should().Be("Plyometric Cardio Circuit is waiting. Show up.");
    }

    [TestMethod]
    public void Body_Rest_SaysRest()
    {
        _messages.Body(new ReminderDay(false, null, true)).Should().Be("Rest day. You earned it.");
    }

    [TestMethod]
    public void Title_IsTheAppName()
    {
        _messages.Title.Should().Be("Show Up");
    }
}
