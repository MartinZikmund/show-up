#if DEBUG
using ShowUp.Core.Entitlements;

namespace ShowUp.Services.Entitlements;

/// <summary>Debug builds only: wraps the real service so Settings can simulate a lapse to Free.</summary>
public sealed class DebugEntitlementOverride : IEntitlementService
{
    private readonly IEntitlementService _inner;
    private bool _simulateFree;

    public DebugEntitlementOverride(IEntitlementService inner)
    {
        _inner = inner;
        _inner.Changed += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
    }

    public bool SimulateFree
    {
        get => _simulateFree;
        set
        {
            if (_simulateFree != value)
            {
                _simulateFree = value;
                Changed?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public bool IsPro => !_simulateFree && _inner.IsPro;

    public bool IsConfirmed => _simulateFree || _inner.IsConfirmed;

    public bool PurchasesAvailable => _inner.PurchasesAvailable;

    public bool OffersLoadFailed => _inner.OffersLoadFailed;

    public event EventHandler? Changed;

    public Task RefreshAsync() => _inner.RefreshAsync();

    public Task<IReadOnlyList<ProOffer>> GetOffersAsync() => _inner.GetOffersAsync();

    public Task<PurchaseOutcome> PurchaseAsync(ProOffer offer) => _inner.PurchaseAsync(offer);

    public Task<bool> RestoreAsync() => _inner.RestoreAsync();

    public Task<Uri?> GetManageSubscriptionUriAsync() => _inner.GetManageSubscriptionUriAsync();
}
#endif
