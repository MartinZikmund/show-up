#if !HAS_UNO
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;
using ShowUp.Core.Entitlements;
using ShowUp.Services.Settings;
using Windows.Services.Store;

namespace ShowUp.Services.Entitlements;

/// <summary>
/// Packaged Windows Pro through Store add-ons: a subscription (monthly/yearly) and a durable lifetime one.
/// The Store reports subscription add-ons as <c>Durable</c> products; <see cref="StoreSku.IsSubscription"/> tells them apart.
/// </summary>
public sealed class MicrosoftStoreEntitlementService : IEntitlementService
{
    private const string AccountSubscriptionsPage = "https://account.microsoft.com/services";

    private readonly (string Id, ProOfferKind Kind)[] _products;
    private readonly IReadOnlySet<string> _proStoreIds;
    private readonly Func<nint> _windowHandle;
    private readonly IAppPreferences _preferences;
    private readonly IStringLocalizer _localizer;
    private readonly TimeProvider _time;
    private readonly ILogger<MicrosoftStoreEntitlementService> _logger;
    private readonly DispatcherQueue? _dispatcher = DispatcherQueue.GetForCurrentThread();
    private readonly SingleFlight _refresh = new();
    private readonly ExpiryWatch _expiry;
    private StoreContext? _context;
    private bool _contextFailed;
    private bool _isPro;
    private volatile bool _confirmed;

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
        _isPro = _proStoreIds.Count > 0 && ProCache.IsPro(preferences.LastKnownPro, preferences.LastKnownProExpires, time.GetUtcNow());
        _expiry = new(time, logger);
        WatchExpiry();
    }

    public bool IsPro => _isPro;

    public bool IsConfirmed => _confirmed;

    /// <summary>Needs configured Store IDs; whether the Store then answers is <see cref="OffersLoadFailed"/>.</summary>
    public bool PurchasesAvailable => _proStoreIds.Count > 0 && !_contextFailed;

    public bool OffersLoadFailed { get; private set; }

    public event EventHandler? Changed;

    public Task RefreshAsync() => _refresh.RunAsync(async () =>
    {
        if (GetContext() is not { } context)
        {
            return;
        }

        try
        {
            var license = await context.GetAppLicenseAsync();
            var addOns = license.AddOnLicenses.Values.Select(l => (BaseId(l.SkuStoreId), l.IsActive, Expiry(l))).ToList();
            var proAddOns = addOns.Where(a => a.Item2 && _proStoreIds.Contains(a.Item1)).ToList();
            // A durable (no expiry) license outlasts any subscription.
            DateTimeOffset? expires = proAddOns.Any(a => a.Item3 is null) ? null : proAddOns.Max(a => a.Item3);
            _confirmed = true;
            SetPro(StoreLicenseMapper.IsPro(addOns, _time.GetUtcNow(), _proStoreIds), expires);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Reading Store licenses failed.");
        }
    });

    public async Task<IReadOnlyList<ProOffer>> GetOffersAsync()
    {
        OffersLoadFailed = false;
        if (_proStoreIds.Count == 0 || GetContext() is not { } context)
        {
            return [];
        }

        try
        {
            var result = await context.GetAssociatedStoreProductsAsync(["Durable"]);
            if (result.ExtendedError is not null)
            {
                _logger.LogInformation(result.ExtendedError, "The Store returned no add-ons.");
                OffersLoadFailed = result.Products.Count == 0;
            }

            List<ProOffer> offers = [];
            foreach (var (id, kind) in _products)
            {
                if (result.Products.Values.FirstOrDefault(p => BaseId(p.StoreId) == id) is { } product)
                {
                    offers.Add(new ProOffer(product.StoreId, _localizer[$"ProOffer{kind}"], product.Price.FormattedPrice, kind));
                }
            }

            OffersLoadFailed |= offers.Count == 0;
            return [.. offers.OrderBy(o => o.Kind)];
        }
        catch (Exception ex)
        {
            _logger.LogInformation(ex, "Loading Store add-ons failed.");
            OffersLoadFailed = true;
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
            var mapped = result.Status switch
            {
                StorePurchaseStatus.Succeeded => BillingResult.Success,
                StorePurchaseStatus.AlreadyPurchased => BillingResult.AlreadyOwned,
                StorePurchaseStatus.NotPurchased => BillingResult.Cancelled,
                StorePurchaseStatus.NetworkError or StorePurchaseStatus.ServerError => BillingResult.NetworkOrServer,
                _ => BillingResult.Other,
            };
            var outcome = await PurchaseResolver.ResolveAsync(mapped, async () =>
            {
                await RefreshAsync();
                return IsPro;
            });
            if (outcome is PurchaseOutcome.Unverified or PurchaseOutcome.Unconfirmed)
            {
                _logger.LogError(result.ExtendedError, "Store purchase of {Offer} ended as {Status} without a Pro license.", offer.Id, result.Status);
            }

            return outcome;
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

    public Task<Uri?> GetManageSubscriptionUriAsync() =>
        Task.FromResult<Uri?>(
            _products.Any(p => p.Kind != ProOfferKind.Lifetime) && !ProCache.IsLifetime(_isPro, _preferences.LastKnownProExpires)
                ? new Uri(AccountSubscriptionsPage)
                : null);

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
            // Raised on a pool thread: hop to the UI thread before touching state.
            context.OfflineLicensesChanged += (_, _) => UiDispatch.Run(_dispatcher, () => _ = RefreshAsync());
            _context = context;
        }
        catch (Exception ex)
        {
            _contextFailed = true;
            _logger.LogInformation(ex, "No Store context; purchases are unavailable.");
        }

        return _context;
    }

    // At the cached end of a subscription, ask the Store again: a renewal keeps Pro, a real lapse (or no answer) locks it.
    private void WatchExpiry() =>
        _expiry.Schedule(
            _isPro ? _preferences.LastKnownProExpires : null,
            () =>
            {
                _confirmed = false;
                return RefreshAsync();
            },
            () => ProCache.IsPro(_preferences.LastKnownPro, _preferences.LastKnownProExpires, _time.GetUtcNow()),
            () => UiDispatch.Run(_dispatcher, () => SetPro(false, null)));

    // A SKU id looks like "9NXXXXXXXXXX/0010"; the configured ids are the product part.
    private static string BaseId(string storeId) => storeId.Split('/')[0];

    // Durable add-ons report a far-future expiry; anything past 50 years counts as no expiry.
    private DateTimeOffset? Expiry(StoreLicense license) =>
        license.ExpirationDate > _time.GetUtcNow().AddYears(50) ? null : license.ExpirationDate;

    private void SetPro(bool value, DateTimeOffset? expires)
    {
        _preferences.LastKnownPro = value;
        _preferences.LastKnownProExpires = value ? expires : null;
        var changed = _isPro != value;
        _isPro = value;
        WatchExpiry();
        if (changed)
        {
            UiDispatch.Run(_dispatcher, () => Changed?.Invoke(this, EventArgs.Empty));
        }
    }
}
#endif
