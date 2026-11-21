namespace ShowUp.Core.Entitlements;

public enum ProOfferKind
{
    Monthly,
    Yearly,
    Lifetime,
}

/// <summary>One way to buy Pro; <see cref="Price"/> is already formatted by the store.</summary>
public sealed partial record ProOffer(string Id, string Title, string Price, ProOfferKind Kind);

public enum PurchaseOutcome
{
    Purchased,
    Cancelled,
    Pending,
    /// <summary>The store took the purchase but Pro has not shown up yet; check back or Restore.</summary>
    Unverified,
    /// <summary>The store didn't answer after checkout (network or server error): we can't tell whether it went through.</summary>
    Unconfirmed,
    Failed,
    Unavailable,
}
