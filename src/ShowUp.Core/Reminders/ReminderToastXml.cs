using System.Xml.Linq;

namespace ShowUp.Core.Reminders;

/// <summary>Toast XML for a Windows scheduled reminder; built with XElement so workout names are escaped.</summary>
public static class ReminderToastXml
{
    /// <summary>The launch argument a click hands back ("action=today", the AppNotification argument format).</summary>
    public const string OpenTodayArgument = "action=today";

    public static string Build(PlannedReminder reminder) =>
        new XElement("toast",
            new XAttribute("launch", OpenTodayArgument),
            new XElement("visual",
                new XElement("binding",
                    new XAttribute("template", "ToastGeneric"),
                    new XElement("text", reminder.Title),
                    new XElement("text", reminder.Body)))).ToString(SaveOptions.DisableFormatting);
}
