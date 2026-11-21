namespace ShowUp.Core.Entitlements;

public interface IEntitlementService
{
    bool IsPro { get; }

    /// <summary>False when this build can't sell Pro (no keys, no Store license, or a platform where Pro is simply unlocked).</summary>
    bool PurchasesAvailable { get; }

    /// <summary>True when the store should have answered but could not (offline, store outage), as opposed to a build that cannot sell Pro.</summary>
    bool OffersLoadFailed { get; }

    event EventHandler? Changed;

    Task RefreshAsync();

    Task<IReadOnlyList<ProOffer>> GetOffersAsync();

    Task<PurchaseOutcome> PurchaseAsync(ProOffer offer);

    Task<bool> RestoreAsync();

    /// <summary>Where the store lets the user manage or cancel the subscription, or null when there is none.</summary>
    Task<Uri?> GetManageSubscriptionUriAsync();
}
