namespace ShowUp.Core.Entitlements;

/// <summary>Whether this build can sync at all (a Supabase project is configured): the paywall and the sync engine both ask this.</summary>
public sealed class CloudSyncAvailability(Func<bool> isConfigured)
{
    public bool IsAvailable => isConfigured();
}
