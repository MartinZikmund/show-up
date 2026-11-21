using ShowUp.Core.Catalog;

namespace ShowUp.Core.Tracking;

public static class ProgressCalculator
{
    public static IReadOnlyList<DayStatus> Build(
        ProgramTrack track,
        IReadOnlyDictionary<string, Workout> workouts,
        EnrollmentCalendar calendar,
        IReadOnlySet<(int Day, int Session)> completed)
    {
        IReadOnlyList<ScheduleDay> days = track.Days;
        if (calendar.DayCount != days.Count)
        {
            throw new ArgumentException("Calendar day count must match the track's day count.", nameof(calendar));
        }

        List<DayStatus> result = new(days.Count);
        for (int i = 0; i < days.Count; i++)
        {
            int sessionCount = days[i].Sessions.Count;
            HashSet<int> done = [];
            foreach ((int day, int session) in completed)
            {
                if (day == i && session >= 0 && session < sessionCount)
                {
                    done.Add(session);
                }
            }

            result.Add(new DayStatus(i, calendar.DateOf(i), sessionCount, done, days[i].IsRestDay(workouts)));
        }

        return result;
    }
}
