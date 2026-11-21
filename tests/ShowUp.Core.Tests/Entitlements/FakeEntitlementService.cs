using ShowUp.Core.Entitlements;

namespace ShowUp.Core.Tests.Entitlements;

internal sealed class FakeEntitlementService : IEntitlementService
{
    public bool IsPro { get; set; }

    public bool PurchasesAvailable { get; set; } = true;

    public List<ProOffer> Offers { get; set; } =
    [
        new("monthly", "Monthly", "$2.99", ProOfferKind.Monthly),
        new("lifetime", "Lifetime", "$19.99", ProOfferKind.Lifetime),
    ];

    public PurchaseOutcome Outcome { get; set; } = PurchaseOutcome.Purchased;

    public bool RestoreResult { get; set; }

    public int PurchaseCalls { get; private set; }

    public TaskCompletionSource? PurchaseGate { get; set; }

    public event EventHandler? Changed;

    public void SetPro(bool value)
    {
        IsPro = value;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public Task RefreshAsync() => Task.CompletedTask;

    public Task<IReadOnlyList<ProOffer>> GetOffersAsync() => Task.FromResult<IReadOnlyList<ProOffer>>(Offers);

    public async Task<PurchaseOutcome> PurchaseAsync(ProOffer offer)
    {
        PurchaseCalls++;
        if (PurchaseGate is not null)
        {
            await PurchaseGate.Task;
        }

        if (Outcome == PurchaseOutcome.Purchased)
        {
            SetPro(true);
        }

        return Outcome;
    }

    public Task<bool> RestoreAsync() => Task.FromResult(RestoreResult);
}
