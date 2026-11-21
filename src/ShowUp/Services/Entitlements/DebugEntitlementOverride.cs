#if DEBUG
using ShowUp.Core.Entitlements;

namespace ShowUp.Services.Entitlements;

public enum ProOverride
{
    Real,
    ForcePro,
    ForceFree,
}

/// <summary>Debug builds only: wraps the real service so Settings can force Pro or Free whatever the store says.</summary>
public sealed class DebugEntitlementOverride : IEntitlementService
{
    private readonly IEntitlementService _inner;
    private ProOverride _override;

    public DebugEntitlementOverride(IEntitlementService inner)
    {
        _inner = inner;
        _inner.Changed += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
    }

    public ProOverride Override
    {
        get => _override;
        set
        {
            if (_override != value)
            {
                _override = value;
                Changed?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public bool IsPro => _override switch
    {
        ProOverride.ForcePro => true,
        ProOverride.ForceFree => false,
        _ => _inner.IsPro,
    };

    public bool IsConfirmed => _override != ProOverride.Real || _inner.IsConfirmed;

    public bool PurchasesAvailable => _inner.PurchasesAvailable;

    public bool OffersLoadFailed => _inner.OffersLoadFailed;

    public event EventHandler? Changed;

    public Task RefreshAsync() => _inner.RefreshAsync();

    public Task SetUserAsync(string? userId) => _inner.SetUserAsync(userId);

    public Task<IReadOnlyList<ProOffer>> GetOffersAsync() => _inner.GetOffersAsync();

    public Task<PurchaseOutcome> PurchaseAsync(ProOffer offer) => _inner.PurchaseAsync(offer);

    public Task<bool> RestoreAsync() => _inner.RestoreAsync();

    public Task<Uri?> GetManageSubscriptionUriAsync() => _inner.GetManageSubscriptionUriAsync();
}
#endif
