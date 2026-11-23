using ShowUp.Core.Tracking;

namespace ShowUp.Core.Marking;

public static class CrossMomentPolicy
{
    /// <summary>Finishing a program always gets its moment; a finished week or phase does unless moments are off.</summary>
    public static bool ShouldShow(CrossMomentMode mode, bool isToday, Milestone? milestone = null) => milestone switch
    {
        { Kind: MilestoneKind.Program } => true,
        not null => mode != CrossMomentMode.Never,
        null => mode switch
        {
            CrossMomentMode.Always => true,
            CrossMomentMode.OnlyToday => isToday,
            _ => false,
        },
    };
}
