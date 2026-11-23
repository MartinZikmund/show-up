using ShowUp.Core.Tracking;

namespace ShowUp.Core.Marking;

/// <summary>The cross moment's text: "Day 10 of 60", "9-day streak" (null at 0) and the progress line.</summary>
/// <param name="Milestone">"Week 3 done", "Month 1 done" or the program line; null when the cross finished nothing bigger than its day.</param>
public sealed record CrossMomentCaption(string Day, string? Streak, string Progress, string? Milestone = null)
{
    /// <summary>The caption line as screen readers should hear it: plain text, no icon.</summary>
    public string Headline => Streak is null ? Day : $"{Day}, {Streak}";

    /// <summary>One sentence for the live-region announcement.</summary>
    public string Announcement => Milestone is null ? $"{Headline}. {Progress}" : $"{Milestone}. {Headline}. {Progress}";

    public static CrossMomentCaption Create(CrossMomentInfo info, IStringLocalizer localizer)
    {
        ArgumentNullException.ThrowIfNull(info);
        ArgumentNullException.ThrowIfNull(localizer);

        var day = localizer["CrossMomentDayFormat", info.DayNumber, info.TotalDays].Value;
        var streak = info.CurrentStreak > 0 ? localizer["CrossMomentStreakFormat", info.CurrentStreak].Value : null;
        var progress = info.Milestone is { Kind: MilestoneKind.Program } ? localizer["CrossMomentProgramComplete"].Value
            : info.SessionsDone < info.SessionsRequired
            ? localizer["CrossMomentProgressFormat", info.SessionsDone, info.SessionsRequired].Value
            : localizer["CrossMomentDayComplete"].Value;

        var milestone = info.Milestone switch
        {
            { Kind: MilestoneKind.Program } => localizer["CrossMomentMilestoneProgram"].Value,
            { Kind: MilestoneKind.Phase } phase => localizer["CrossMomentMilestonePhase", phase.PhaseName].Value,
            { Kind: MilestoneKind.Week } week => localizer["CrossMomentMilestoneWeek", week.WeekNumber].Value,
            _ => null,
        };

        return new(day, streak, progress, milestone);
    }
}
