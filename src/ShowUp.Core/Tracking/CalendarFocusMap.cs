namespace ShowUp.Core.Tracking;

public enum CalendarFocusMove
{
    Left,
    Right,
    Up,
    Down,
    Home,
    End,
}

/// <summary>Where keyboard focus goes in the calendar grid: one row per week, rows of different lengths.</summary>
public static class CalendarFocusMap
{
    /// <returns>The target cell, or null when the move leaves the grid (focus then moves on normally).</returns>
    public static (int Week, int Day)? Move(int week, int day, CalendarFocusMove move, IReadOnlyList<int> weekLengths)
    {
        if (week < 0 || week >= weekLengths.Count || day < 0 || day >= weekLengths[week])
        {
            return null;
        }

        switch (move)
        {
            case CalendarFocusMove.Home:
                return (week, 0);
            case CalendarFocusMove.End:
                return (week, weekLengths[week] - 1);
            case CalendarFocusMove.Left when day > 0:
                return (week, day - 1);
            case CalendarFocusMove.Right when day < weekLengths[week] - 1:
                return (week, day + 1);
        }

        // Everything else changes row; empty weeks are stepped over.
        int step = move is CalendarFocusMove.Left or CalendarFocusMove.Up ? -1 : 1;
        for (int target = week + step; target >= 0 && target < weekLengths.Count; target += step)
        {
            int length = weekLengths[target];
            if (length == 0)
            {
                continue;
            }

            return move switch
            {
                CalendarFocusMove.Left => (target, length - 1),
                CalendarFocusMove.Right => (target, 0),
                _ => (target, Math.Min(day, length - 1)),
            };
        }

        return null;
    }
}
