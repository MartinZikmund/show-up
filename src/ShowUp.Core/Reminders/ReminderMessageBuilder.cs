namespace ShowUp.Core.Reminders;

/// <summary>Notification texts. Without a localizer it speaks English, which is what the planner tests read.</summary>
public sealed class ReminderMessageBuilder(IStringLocalizer? localizer = null)
{
    public static ReminderMessageBuilder English { get; } = new();

    public string Title => Text("ReminderTitle", "Show Up");

    public string Body(ReminderDay day) =>
        day.IsRest || day.WorkoutTitle is not { Length: > 0 } title
            ? Text("ReminderRestBody", "Rest day. You earned it.")
            : localizer is null ? $"{title} is waiting. Show up." : localizer["ReminderWorkoutBody", title].Value;

    private string Text(string key, string fallback) => localizer is null ? fallback : localizer[key].Value;
}
