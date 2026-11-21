namespace ShowUp.Core.Marking;

/// <summary>The cross moment's text: "Day 10 of 60", "9-day streak" (null at 0) and the progress line.</summary>
public sealed record CrossMomentCaption(string Day, string? Streak, string Progress)
{
    /// <summary>The caption line as screen readers should hear it: plain text, no icon.</summary>
    public string Headline => Streak is null ? Day : $"{Day}, {Streak}";

    /// <summary>One sentence for the live-region announcement.</summary>
    public string Announcement => $"{Headline}. {Progress}";

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
