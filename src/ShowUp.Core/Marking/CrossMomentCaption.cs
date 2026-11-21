namespace ShowUp.Core.Marking;

/// <summary>The cross moment's text: "Day 10 of 60", "9-day streak" (null at 0) and the progress line.</summary>
public sealed record CrossMomentCaption(string Day, string? Streak, string Progress)
{
    public static CrossMomentCaption Create(CrossMomentInfo info, IStringLocalizer localizer)
    {
        ArgumentNullException.ThrowIfNull(info);
        ArgumentNullException.ThrowIfNull(localizer);

        var day = localizer["CrossMomentDayFormat", info.DayNumber, info.TotalDays].Value;
        var streak = info.CurrentStreak > 0 ? localizer["CrossMomentStreakFormat", info.CurrentStreak].Value : null;
        var progress = info.SessionsDone < info.SessionsRequired
            ? localizer["CrossMomentProgressFormat", info.SessionsDone, info.SessionsRequired].Value
            : localizer["CrossMomentDayComplete"].Value;

        return new(day, streak, progress);
    }
}
