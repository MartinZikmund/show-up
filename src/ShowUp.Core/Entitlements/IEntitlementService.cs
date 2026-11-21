namespace ShowUp.Core.Entitlements;

public interface IEntitlementService
{
    bool IsPro { get; }

    /// <summary>
    /// True when <see cref="IsPro"/> comes from the store itself (a successful answer this session) or the platform needs no store.
    /// False while it is only the cache, e.g. offline or after a timed-out check, so a lapse seen then isn't acted on destructively.
    /// </summary>
    bool IsConfirmed { get; }

    /// <summary>False when this build can't sell Pro (no keys, no Store license, or a platform where Pro is simply unlocked).</summary>
    bool PurchasesAvailable { get; }

    /// <summary>True when the store should have answered but could not (offline, store outage), as opposed to a build that cannot sell Pro.</summary>
    bool OffersLoadFailed { get; }

    event EventHandler? Changed;

    Task RefreshAsync();

    /// <summary>
    /// Tells the store who is signed in (null: nobody) and switches the cached Pro state to that account's.
    /// Only account-based stores (RevenueCat) act on it; the Microsoft Store and the unlocked platforms ignore it.
    /// </summary>
    Task SetUserAsync(string? userId);

    Task<IReadOnlyList<ProOffer>> GetOffersAsync();

    Task<PurchaseOutcome> PurchaseAsync(ProOffer offer);

    Task<bool> RestoreAsync();

    /// <summary>Where the store lets the user manage or cancel the subscription, or null when there is none.</summary>
    Task<Uri?> GetManageSubscriptionUriAsync();
}
