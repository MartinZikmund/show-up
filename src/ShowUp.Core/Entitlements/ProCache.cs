namespace ShowUp.Core.Entitlements;

/// <summary>Reads the last known Pro state back for an offline launch, honoring the subscription's end date.</summary>
public static class ProCache
{
    public static bool IsPro(bool lastKnownPro, DateTimeOffset? expires, DateTimeOffset now) =>
        lastKnownPro && (expires is null || expires > now);

    /// <summary>Pro with no end date: a lifetime purchase, which has no subscription to manage.</summary>
    public static bool IsLifetime(bool isPro, DateTimeOffset? expires) => isPro && expires is null;
}
