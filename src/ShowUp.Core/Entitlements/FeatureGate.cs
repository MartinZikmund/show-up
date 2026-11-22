namespace ShowUp.Core.Entitlements;

/// <summary>What the Free tier gets; Pro unlocks everything.</summary>
public static class FeatureGate
{
    public const int FreeReminders = 1;
    public const int ProReminders = 20;

    /// <summary>Whether this build offers the feature; cloud sync needs a Supabase project, otherwise the paywall says "Coming soon".</summary>
    public static bool IsAvailable(ProFeature feature, CloudSyncAvailability cloudSync) => feature is not ProFeature.CloudSync || cloudSync.IsAvailable;

    public static bool CanUse(ProFeature feature, bool isPro) => isPro;

    public static int MaxActivePrograms(bool isPro) => isPro ? int.MaxValue : 1;

    /// <summary>Lapsed users keep their stored choice (for re-subscribing) but see the default ink.</summary>
    public static ShowUp.Core.Marking.MarkerColor EffectiveMarkerColor(ShowUp.Core.Marking.MarkerColor stored, bool isPro) =>
        CanUse(ProFeature.Personalization, isPro) ? stored : ShowUp.Core.Marking.MarkerColor.Auto;

    public static int MaxReminders(bool isPro) => isPro ? ProReminders : FreeReminders;
}
