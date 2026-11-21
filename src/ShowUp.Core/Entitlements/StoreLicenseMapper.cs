namespace ShowUp.Core.Entitlements;

/// <summary>Turns Microsoft Store add-on licenses into the Pro flag; kept pure so it is testable off Windows.</summary>
public static class StoreLicenseMapper
{
    /// <summary>A null expiry means a durable (lifetime) license.</summary>
    public static bool IsPro(
        IEnumerable<(string StoreId, bool IsActive, DateTimeOffset? Expires)> addOns,
        DateTimeOffset now,
        IReadOnlySet<string> proStoreIds) =>
        addOns.Any(a => a.IsActive && proStoreIds.Contains(a.StoreId) && (a.Expires is null || a.Expires > now));
}
