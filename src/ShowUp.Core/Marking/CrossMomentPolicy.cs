namespace ShowUp.Core.Marking;

public static class CrossMomentPolicy
{
    public static bool ShouldShow(CrossMomentMode mode, bool isToday) => mode switch
    {
        CrossMomentMode.Always => true,
        CrossMomentMode.OnlyToday => isToday,
        _ => false,
    };
}
