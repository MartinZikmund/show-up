using System.Xml.Linq;
using AwesomeAssertions;
using ShowUp.Core.Reminders;

namespace ShowUp.Core.Tests.Reminders;

[TestClass]
public sealed class ReminderToastXmlTests
{
    private static PlannedReminder Reminder(string body) => new(Guid.NewGuid(), DateTimeOffset.UtcNow.AddHours(1), "Show Up", body);

    [TestMethod]
    public void Build_WorkoutWithMarkupCharacters_IsEscapedAndRoundTrips()
    {
        var xml = ReminderToastXml.Build(Reminder("Cardio & <Core> is waiting. Show up."));

        xml.Should().Contain("Cardio &amp; &lt;Core&gt;");
        XDocument.Parse(xml).Descendants("text").Select(t => t.Value).Should().Equal("Show Up", "Cardio & <Core> is waiting. Show up.");
    }

    [TestMethod]
    public void Build_Always_OpensTodayOnClick()
    {
        var toast = XDocument.Parse(ReminderToastXml.Build(Reminder("Run is waiting. Show up."))).Root!;

        ((string?)toast.Attribute("launch")).Should().Be("action=today");
        ((string?)toast.Element("visual")!.Element("binding")!.Attribute("template")).Should().Be("ToastGeneric");
    }
}
