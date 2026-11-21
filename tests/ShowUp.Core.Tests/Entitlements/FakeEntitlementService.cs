using ShowUp.Core.Entitlements;

namespace ShowUp.Core.Tests.Entitlements;

internal sealed class FakeEntitlementService : IEntitlementService
{
    public bool IsPro { get; set; }

    public bool IsConfirmed { get; set; } = true;

    public bool PurchasesAvailable { get; set; } = true;

    public List<ProOffer> Offers { get; set; } =
    [
        new("monthly", "Monthly", "$2.99", ProOfferKind.Monthly),
        new("lifetime", "Lifetime", "$19.99", ProOfferKind.Lifetime),
    ];

    public PurchaseOutcome Outcome { get; set; } = PurchaseOutcome.Purchased;

    public bool RestoreResult { get; set; }

    public bool OffersLoadFailed { get; set; }

    public Uri? ManageUri { get; set; }

    public int PurchaseCalls { get; private set; }

    public TaskCompletionSource? PurchaseGate { get; set; }

    public event EventHandler? Changed;

    public void SetPro(bool value)
    {
        IsPro = value;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Runs on refresh, e.g. a renewal the store reports.</summary>
    public Func<Task>? OnRefresh { get; set; }

    public int RefreshCalls { get; private set; }

    public List<string?> Users { get; } = [];

    public Task SetUserAsync(string? userId)
    {
        Users.Add(userId);
        return Task.CompletedTask;
    }

    public Task RefreshAsync()
    {
        RefreshCalls++;
        return OnRefresh?.Invoke() ?? Task.CompletedTask;
    }

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

    public Task<Uri?> GetManageSubscriptionUriAsync() => Task.FromResult(ManageUri);
}

internal sealed class RecordingLauncher : ShowUp.Services.ILauncherService
{
    public List<Uri> Launched { get; } = [];

    public Task<bool> LaunchUriAsync(Uri uri)
    {
        Launched.Add(uri);
        return Task.FromResult(true);
    }
}
