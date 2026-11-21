using ShowUp.Core.Entitlements;

namespace ShowUp.Services.Entitlements;

/// <summary>Desktop Skia and WebAssembly: no store to sell Pro through, so everything is unlocked.</summary>
public sealed class UnlockedEntitlementService : IEntitlementService
{
    public bool IsPro => true;

    public bool PurchasesAvailable => false;

#pragma warning disable CS0067 // Never raised: the state never changes.
    public event EventHandler? Changed;
#pragma warning restore CS0067

    public Task RefreshAsync() => Task.CompletedTask;

    public Task<IReadOnlyList<ProOffer>> GetOffersAsync() => Task.FromResult<IReadOnlyList<ProOffer>>([]);

    public Task<PurchaseOutcome> PurchaseAsync(ProOffer offer) => Task.FromResult(PurchaseOutcome.Unavailable);

    public Task<bool> RestoreAsync() => Task.FromResult(true);
}
