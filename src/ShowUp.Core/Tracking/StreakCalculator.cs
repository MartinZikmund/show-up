namespace ShowUp.Core.Tracking;

public static class StreakCalculator
{
    public static int Current(IReadOnlyList<DayStatus> days, DateOnly today)
    {
        int streak = 0;
        foreach (DayStatus day in days.Where(d => d.Date <= today).OrderByDescending(d => d.Date))
        {
            if (day.IsRestDay)
            {
                continue;
            }

            if (day.Progress == DayProgress.Complete)
            {
                streak++;
            }
            else if (day.Date < today)
            {
                break;
            }
        }

        return streak;
    }

    public static int Longest(IReadOnlyList<DayStatus> days, DateOnly today)
    {
        int longest = 0;
        int run = 0;
        foreach (DayStatus day in days.Where(d => d.Date <= today).OrderBy(d => d.Date))
        {
            if (day.IsRestDay)
            {
                continue;
            }

            if (day.Progress == DayProgress.Complete)
            {
                run++;
                longest = Math.Max(longest, run);
            }
            else
            {
                run = 0;
            }
        }

        return longest;
    }
}
