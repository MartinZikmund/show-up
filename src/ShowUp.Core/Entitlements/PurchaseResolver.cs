namespace ShowUp.Core.Entitlements;

/// <summary>What a store said about a purchase attempt, before we check it actually unlocked Pro.</summary>
public enum BillingResult
{
    Success,
    AlreadyOwned,
    Cancelled,
    Pending,
    NetworkOrServer,
    Other,
}

public static class PurchaseResolver
{
    /// <summary>
    /// Turns a store result into an outcome. A purchase only counts as <see cref="PurchaseOutcome.Purchased"/> once
    /// <paramref name="refreshIsPro"/> confirms the entitlement; otherwise the user is told to check back or Restore. A network or server error can't say whether the purchase
    /// went through, so it's <see cref="PurchaseOutcome.Unconfirmed"/>.
    /// </summary>
    public static async Task<PurchaseOutcome> ResolveAsync(BillingResult result, Func<Task<bool>> refreshIsPro)
    {
        switch (result)
        {
            case BillingResult.Success:
            case BillingResult.AlreadyOwned:
                return await refreshIsPro() ? PurchaseOutcome.Purchased : PurchaseOutcome.Unverified;
            case BillingResult.Cancelled:
                return PurchaseOutcome.Cancelled;
            case BillingResult.Pending:
                return PurchaseOutcome.Pending;
            case BillingResult.NetworkOrServer:
                return PurchaseOutcome.Unconfirmed;
            default:
                return PurchaseOutcome.Failed;
        }
    }
}
