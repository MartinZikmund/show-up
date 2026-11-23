using ShowUp.Core.Catalog;

namespace ShowUp.Core.Tracking;

/// <summary>Tells when a cross finished a week, a phase or the whole program.</summary>
public static class MilestoneDetector
{
    /// <summary>
    /// The largest unit around <paramref name="crossedDayIndex"/> that was open in <paramref name="before"/> and is
    /// complete in <paramref name="after"/>; null when the cross finished nothing. A one-phase program has no phase milestone.
    /// </summary>
    public static Milestone? Detect(ProgramTrack track, IReadOnlyList<DayStatus> before, IReadOnlyList<DayStatus> after, int crossedDayIndex)
    {
        int dayCount = track.Phases.Sum(p => p.Weeks.Sum(w => w.Days.Count));
        int weekStart = 0;
        int weekNumber = 0;
        foreach (ProgramPhase phase in track.Phases)
        {
            int phaseStart = weekStart;
            int phaseEnd = phaseStart + phase.Weeks.Sum(w => w.Days.Count);
            foreach (ScheduleWeek week in phase.Weeks)
            {
                weekNumber++;
                int weekEnd = weekStart + week.Days.Count;
                if (crossedDayIndex >= weekStart && crossedDayIndex < weekEnd)
                {
                    MilestoneKind? kind =
                        Completed(before, after, 0, dayCount) ? MilestoneKind.Program
                        : track.Phases.Count > 1 && Completed(before, after, phaseStart, phaseEnd) ? MilestoneKind.Phase
                        : Completed(before, after, weekStart, weekEnd) ? MilestoneKind.Week
                        : null;
                    return kind is { } completed ? new Milestone(completed, phase.Name, weekNumber) : null;
                }

                weekStart = weekEnd;
            }
        }

        return null;
    }

    /// <summary>Every required session of every day is crossed. A program with nothing to cross is never complete.</summary>
    public static bool IsProgramComplete(IReadOnlyList<DayStatus> days) => IsComplete(days, 0, days.Count);

    private static bool Completed(IReadOnlyList<DayStatus> before, IReadOnlyList<DayStatus> after, int start, int end) =>
        !IsComplete(before, start, end) && IsComplete(after, start, end);

    // Rest days and all-optional days never hold a unit open, but something in it must be crossed.
    private static bool IsComplete(IReadOnlyList<DayStatus> days, int start, int end)
    {
        if (end > days.Count)
        {
            return false;
        }

        bool anyCrossed = false;
        for (int i = start; i < end; i++)
        {
            if (days[i].Progress == DayProgress.Complete)
            {
                anyCrossed = true;
            }
            else if (!days[i].CountsAsRest)
            {
                return false;
            }
        }

        return anyCrossed;
    }
}
