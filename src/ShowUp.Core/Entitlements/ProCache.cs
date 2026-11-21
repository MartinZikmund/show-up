namespace ShowUp.Core.Entitlements;

/// <summary>Reads the last known Pro state back for an offline launch, honoring the subscription's end date.</summary>
public static class ProCache
{
    public static bool IsPro(bool lastKnownPro, DateTimeOffset? expires, DateTimeOffset now) =>
        lastKnownPro && (expires is null || expires > now);
}
