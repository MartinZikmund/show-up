namespace ShowUp.Core.Reminders;

/// <summary>Notification texts, localized.</summary>
public sealed class ReminderMessageBuilder(IStringLocalizer localizer)
{
    public string Title => localizer["ReminderTitle"].Value;

    public string Body(ReminderDay day) =>
        day.IsRest || day.WorkoutTitle is not { Length: > 0 } title
            ? localizer["ReminderRestBody"].Value
            : localizer["ReminderWorkoutBody", title].Value;
}
