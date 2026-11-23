using ShowUp.Core.Services;
using ShowUp.Services.Settings;
using ShowUp.Core.Tracking;

namespace ShowUp.Core.Marking;

/// <summary>One session about to be crossed: the day around it, as the calendar and Today both know it.</summary>
/// <param name="EnrollmentId">The enrollment the day belongs to; several programs can run at once.</param>
/// <param name="DaySeed">The marker seed of the day's earliest completion; unused for a day's first cross.</param>
/// <param name="AllStatuses">Every day of the enrollment, for the streak the cross moment shows.</param>
public sealed record CrossTarget(
    Guid EnrollmentId,
    int DayIndex,
    int Session,
    string SessionName,
    bool IsToday,
    int DaySeed,
    DayStatus Status,
    IReadOnlyList<DayStatus> AllStatuses,
    IReadOnlyList<CompletedSession> DayCompletions,
    DateOnly Today);

/// <summary>What the caller does around the write: guard its own refresh echo, reload, and hold marks still while a moment plays.</summary>
public sealed record CrossHooks(Func<Func<Task>, Task> Write, Func<Task> Refresh, Action<bool> SetAnimateMarks);

/// <summary>The crossing flow shared by the calendar and Today: draw or auto marker, then the cross moment.</summary>
public interface ICrossingCoordinator
{
    /// <returns>False when the user cancelled the draw.</returns>
    Task<bool> CrossAsync(CrossTarget target, CrossHooks hooks);
}

public sealed class CrossingCoordinator(
    IAppPreferences preferences,
    ICrossMoment crossMoment,
    IMarkingFeedback feedback,
    IActiveEnrollmentService enrollments,
    IJournalService journal,
    IJournalDialogs journalDialogs)
    : ICrossingCoordinator
{
    public async Task<bool> CrossAsync(CrossTarget target, CrossHooks hooks)
    {
        var (before, afterAuto, afterDrawn) = MarkerAppearance.Outcomes(
            target.Status,
            target.DayCompletions.Where(c => c.InkJson is null).Select(c => c.SessionIndex).ToHashSet(),
            target.Session);

        // The id is made up front: a day's first completion picks the wobble, and the moment must draw the same X.
        var completionId = Guid.NewGuid();
        var seed = target.DayCompletions.Count == 0 ? MarkerGeometry.SeedFrom(completionId) : target.DaySeed;
        var info = MomentInfo(target, before, afterAuto, seed) with
        {
            LogResults = await LogResultsAsync(target),
            Milestone = await MilestoneAsync(target),
        };

        if (preferences.CrossOffStyle == CrossOffStyle.DrawYourself)
        {
            List<string> priorInks = [.. target.DayCompletions.Where(c => c.InkJson is not null).Select(c => c.InkJson!)];
            var result = await crossMoment.DrawAsync(new CrossMomentDrawRequest(info, priorInks, afterDrawn));
            if (!result.Crossed)
            {
                return false;
            }

            hooks.SetAnimateMarks(false);
            await hooks.Write(() => enrollments.CrossAsync(target.EnrollmentId, target.DayIndex, target.Session, result.InkJson, completionId));
            await hooks.Refresh();
            return true;
        }

        var showMoment = CrossMomentPolicy.ShouldShow(preferences.CrossMomentMode, target.IsToday, info.Milestone);
        hooks.SetAnimateMarks(!showMoment);
        await hooks.Write(() => enrollments.CrossAsync(target.EnrollmentId, target.DayIndex, target.Session, completionId: completionId));
        await hooks.Refresh();
        if (showMoment)
        {
            await crossMoment.ShowAsync(info);
        }
        else if (afterAuto == before)
        {
            feedback.SessionTicked();
        }

        return true;
    }

    private async Task<Func<Task>?> LogResultsAsync(CrossTarget target) =>
        await journal.HasMetricsAsync(target.EnrollmentId, target.DayIndex, target.Session)
            ? () => journalDialogs.LogResultsAsync(target.EnrollmentId, target.DayIndex, target.Session)
            : null;

    // Only this cross can earn a milestone: a completion that arrives by sync never passes through here.
    private async Task<Milestone?> MilestoneAsync(CrossTarget target) =>
        await enrollments.GetAsync(target.EnrollmentId) is { } active
            ? MilestoneDetector.Detect(active.Track, target.AllStatuses, StatusesAfter(target), target.DayIndex)
            : null;

    private static List<DayStatus> StatusesAfter(CrossTarget target)
    {
        var status = target.Status;
        List<DayStatus> statuses = [.. target.AllStatuses];
        statuses[target.DayIndex] = status with { CompletedSessions = new HashSet<int>(status.CompletedSessions) { target.Session } };
        return statuses;
    }

    private static CrossMomentInfo MomentInfo(CrossTarget target, int before, int afterAuto, int seed)
    {
        var statusesAfter = StatusesAfter(target);
        var after = statusesAfter[target.DayIndex];

        return new CrossMomentInfo(
            target.DayIndex + 1,
            target.AllStatuses.Count,
            target.SessionName,
            before,
            afterAuto,
            seed,
            after.CompletedSessions.Count(i => !after.OptionalSessions.Contains(i)),
            after.RequiredCount,
            StreakCalculator.Current(statusesAfter, target.Today));
    }
}
