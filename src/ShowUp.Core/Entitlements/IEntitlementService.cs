namespace ShowUp.Core.Entitlements;

public interface IEntitlementService
{
    bool IsPro { get; }

    /// <summary>False when this build can't sell Pro (no keys, no Store license, or a platform where Pro is simply unlocked).</summary>
    bool PurchasesAvailable { get; }

    event EventHandler? Changed;

    Task RefreshAsync();

    Task<IReadOnlyList<ProOffer>> GetOffersAsync();

    Task<PurchaseOutcome> PurchaseAsync(ProOffer offer);

    Task<bool> RestoreAsync();
}
