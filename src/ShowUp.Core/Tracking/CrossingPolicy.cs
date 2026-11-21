namespace ShowUp.Core.Tracking;

public static class CrossingPolicy
{
    public static bool CanCross(DateOnly dayDate, DateOnly today) => dayDate <= today;
}
