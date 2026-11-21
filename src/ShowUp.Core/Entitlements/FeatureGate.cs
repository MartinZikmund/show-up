namespace ShowUp.Core.Entitlements;

/// <summary>What the Free tier gets; Pro unlocks everything.</summary>
public static class FeatureGate
{
    public const int FreeReminders = 1;
    public const int ProReminders = 20;

    public static bool CanUse(ProFeature feature, bool isPro) => isPro;

    public static int MaxActivePrograms(bool isPro) => isPro ? int.MaxValue : 1;

    public static int MaxReminders(bool isPro) => isPro ? ProReminders : FreeReminders;
}
