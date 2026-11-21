#if !HAS_UNO
using Microsoft.Extensions.Logging;
using ShowUp.Core.Entitlements;
using ShowUp.Services.Settings;
using Windows.Services.Store;

namespace ShowUp.Services.Entitlements;

/// <summary>Packaged Windows Pro through Store add-ons: a subscription (monthly/yearly) and a durable lifetime one.</summary>
public sealed class MicrosoftStoreEntitlementService : IEntitlementService
{
    private readonly (string Id, ProOfferKind Kind)[] _products;
    private readonly IReadOnlySet<string> _proStoreIds;
    private readonly Func<nint> _windowHandle;
    private readonly IAppPreferences _preferences;
    private readonly IStringLocalizer _localizer;
    private readonly TimeProvider _time;
    private readonly ILogger<MicrosoftStoreEntitlementService> _logger;
    private StoreContext? _context;
    private bool _contextFailed;

    public MicrosoftStoreEntitlementService(
        string monthlyId,
        string yearlyId,
        string lifetimeId,
        Func<nint> windowHandle,
        IAppPreferences preferences,
        IStringLocalizer localizer,
        TimeProvider time,
        ILogger<MicrosoftStoreEntitlementService> logger)
    {
        _products = new[]
        {
            (monthlyId, ProOfferKind.Monthly),
            (yearlyId, ProOfferKind.Yearly),
            (lifetimeId, ProOfferKind.Lifetime),
        }.Where(p => p.Item1.Length > 0).ToArray();
        _proStoreIds = _products.Select(p => p.Id).ToHashSet();
        _windowHandle = windowHandle;
        _preferences = preferences;
        _localizer = localizer;
        _time = time;
        _logger = logger;
        IsPro = preferences.LastKnownPro && _proStoreIds.Count > 0;
    }

    public bool IsPro { get; private set; }

    /// <summary>True once the Store has handed us at least one of the configured add-ons.</summary>
    public bool PurchasesAvailable { get; private set; }

    public event EventHandler? Changed;

    public async Task RefreshAsync()
    {
        if (GetContext() is not { } context)
        {
            return;
        }

        try
        {
            var license = await context.GetAppLicenseAsync();
            var addOns = license.AddOnLicenses.Values.Select(l => (BaseId(l.SkuStoreId), l.IsActive, Expiry(l)));
            SetPro(StoreLicenseMapper.IsPro(addOns, _time.GetUtcNow(), _proStoreIds));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Reading Store licenses failed.");
        }
    }

    public async Task<IReadOnlyList<ProOffer>> GetOffersAsync()
    {
        if (_proStoreIds.Count == 0 || GetContext() is not { } context)
        {
            return [];
        }

        try
        {
            var result = await context.GetAssociatedStoreProductsAsync(["Durable", "Subscription"]);
            if (result.ExtendedError is not null)
            {
                _logger.LogInformation(result.ExtendedError, "The Store returned no add-ons.");
            }

            List<ProOffer> offers = [];
            foreach (var (id, kind) in _products)
            {
                if (result.Products.Values.FirstOrDefault(p => BaseId(p.StoreId) == id) is { } product)
                {
                    offers.Add(new ProOffer(product.StoreId, _localizer[$"ProOffer{kind}"], product.Price.FormattedPrice, kind));
                }
            }

            PurchasesAvailable = offers.Count > 0;
            return [.. offers.OrderBy(o => o.Kind)];
        }
        catch (Exception ex)
        {
            _logger.LogInformation(ex, "Loading Store add-ons failed; purchases are unavailable.");
            PurchasesAvailable = false;
            return [];
        }
    }

    public async Task<PurchaseOutcome> PurchaseAsync(ProOffer offer)
    {
        if (GetContext() is not { } context)
        {
            return PurchaseOutcome.Unavailable;
        }

        try
        {
            var result = await context.RequestPurchaseAsync(offer.Id);
            switch (result.Status)
            {
                case StorePurchaseStatus.Succeeded:
                case StorePurchaseStatus.AlreadyPurchased:
                    await RefreshAsync();
                    return IsPro ? PurchaseOutcome.Purchased : PurchaseOutcome.Pending;
                case StorePurchaseStatus.NotPurchased:
                    return PurchaseOutcome.Cancelled;
                default:
                    return PurchaseOutcome.Failed;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Purchasing {Offer} failed.", offer.Id);
            return PurchaseOutcome.Failed;
        }
    }

    public async Task<bool> RestoreAsync()
    {
        await RefreshAsync();
        return IsPro;
    }

    private StoreContext? GetContext()
    {
        if (_context is not null || _contextFailed || _proStoreIds.Count == 0)
        {
            return _context;
        }

        try
        {
            var context = StoreContext.GetDefault();
            WinRT.Interop.InitializeWithWindow.Initialize(context, _windowHandle());
            context.OfflineLicensesChanged += (_, _) => _ = RefreshAsync();
            _context = context;
        }
        catch (Exception ex)
        {
            _contextFailed = true;
            _logger.LogInformation(ex, "No Store context; purchases are unavailable.");
        }

        return _context;
    }

    // A SKU id looks like "9NXXXXXXXXXX/0010"; the configured ids are the product part.
    private static string BaseId(string storeId) => storeId.Split('/')[0];

    // Durable add-ons report a far-future expiry; anything past 50 years counts as no expiry.
    private DateTimeOffset? Expiry(StoreLicense license) =>
        license.ExpirationDate > _time.GetUtcNow().AddYears(50) ? null : license.ExpirationDate;

    private void SetPro(bool value)
    {
        _preferences.LastKnownPro = value;
        if (IsPro != value)
        {
            IsPro = value;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }
}
#endif
