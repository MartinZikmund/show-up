#if __ANDROID__ || __IOS__
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;
using ShowUp.Core.Entitlements;
using ShowUp.Services.Settings;
using Uno.RevenueCat.Enums;
using Uno.RevenueCat.Extensions;
using Uno.RevenueCat.Models;
using Uno.RevenueCat.Services;

namespace ShowUp.Services.Entitlements;

/// <summary>Android and iOS Pro through RevenueCat; the last known state is cached so offline launches stay Pro until it lapses.</summary>
public sealed class RevenueCatEntitlementService : IEntitlementService
{
    private const string EntitlementId = "pro";

    private readonly IRevenueCatBilling _billing;
    private readonly IAppPreferences _preferences;
    private readonly IStringLocalizer _localizer;
    private readonly TimeProvider _time;
    private readonly ILogger<RevenueCatEntitlementService> _logger;
    private readonly string _apiKey;
    private readonly DispatcherQueue? _dispatcher = DispatcherQueue.GetForCurrentThread();
    private readonly SingleFlight _refresh = new();
    private readonly Dictionary<string, PackageDto> _packages = [];
    private bool _initialized;

    public RevenueCatEntitlementService(
        IRevenueCatBilling billing,
        IAppPreferences preferences,
        IStringLocalizer localizer,
        TimeProvider time,
        ILogger<RevenueCatEntitlementService> logger,
        string apiKey)
    {
        _billing = billing;
        _preferences = preferences;
        _localizer = localizer;
        _time = time;
        _logger = logger;
        _apiKey = apiKey;
    }

    public bool IsPro => ProCache.IsPro(_preferences.LastKnownPro, _preferences.LastKnownProExpires, _time.GetUtcNow());

    public bool PurchasesAvailable => _apiKey.Length > 0;

    public bool OffersLoadFailed { get; private set; }

    public event EventHandler? Changed;

    public Task RefreshAsync() => _refresh.RunAsync(async () =>
    {
        if (!EnsureInitialized())
        {
            return;
        }

        try
        {
            var result = await _billing.GetCustomerInfoAsync(CustomerInfoFetchPolicy.FetchCurrent);
            if (result.IsSuccess)
            {
                Apply(result.Value);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Refreshing the Pro entitlement failed.");
        }
    });

    public async Task<IReadOnlyList<ProOffer>> GetOffersAsync()
    {
        OffersLoadFailed = false;
        if (!EnsureInitialized())
        {
            return [];
        }

        try
        {
            var result = await _billing.GetOfferingsAsync();
            if (!result.IsSuccess)
            {
                OffersLoadFailed = true;
                return [];
            }

            _packages.Clear();
            List<ProOffer> offers = [];
            foreach (var package in result.Value!.GetCurrent()?.AvailablePackages ?? [])
            {
                if (KindOf(package.Identifier) is not { } kind)
                {
                    continue;
                }

                _packages[package.Identifier] = package;
                offers.Add(new ProOffer(package.Identifier, _localizer[$"ProOffer{kind}"], package.Product.Pricing.PriceLocalized, kind));
            }

            return [.. offers.OrderBy(o => o.Kind)];
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Loading Pro offers failed.");
            OffersLoadFailed = true;
            return [];
        }
    }

    public async Task<PurchaseOutcome> PurchaseAsync(ProOffer offer)
    {
        if (!EnsureInitialized() || !_packages.TryGetValue(offer.Id, out var package))
        {
            return PurchaseOutcome.Unavailable;
        }

        try
        {
            var result = await _billing.PurchaseProductAsync(package);
            if (result.IsSuccess)
            {
                Apply(result.Value);
                if (!IsPro)
                {
                    _logger.LogError("Purchase of {Offer} succeeded but the customer has no active '{Entitlement}' entitlement.", offer.Id, EntitlementId);
                }

                return await PurchaseResolver.ResolveAsync(BillingResult.Success, () => Task.FromResult(IsPro));
            }

            var mapped = result.Error switch
            {
                PurchaseErrorStatus.PurchaseCancelledError => BillingResult.Cancelled,
                PurchaseErrorStatus.PaymentPendingError => BillingResult.Pending,
                PurchaseErrorStatus.ProductAlreadyPurchasedError => BillingResult.AlreadyOwned,
                PurchaseErrorStatus.NetworkError or PurchaseErrorStatus.StoreProblemError => BillingResult.NetworkOrServer,
                _ => BillingResult.Other,
            };
            return await PurchaseResolver.ResolveAsync(mapped, RestoreAsync);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Purchasing {Offer} failed.", offer.Id);
            return PurchaseOutcome.Failed;
        }
    }

    public async Task<bool> RestoreAsync()
    {
        if (!EnsureInitialized())
        {
            return false;
        }

        try
        {
            var result = await _billing.RestoreTransactionsAsync();
            if (result.IsSuccess)
            {
                Apply(result.Value);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Restoring purchases failed.");
        }

        return IsPro;
    }

    public async Task<Uri?> GetManageSubscriptionUriAsync()
    {
        if (!EnsureInitialized())
        {
            return null;
        }

        try
        {
            var result = await _billing.GetManagementSubscriptionUrlAsync();
            return result.IsSuccess && Uri.TryCreate(result.Value, UriKind.Absolute, out var uri) ? uri : null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Reading the management URL failed.");
            return null;
        }
    }

    private bool EnsureInitialized()
    {
        if (_apiKey.Length == 0)
        {
            return false;
        }

        if (!_initialized)
        {
            _billing.Initialize(_apiKey);
            _initialized = true;
        }

        return true;
    }

    private static ProOfferKind? KindOf(string identifier) => identifier switch
    {
        DefaultPackageIdentifier.Monthly => ProOfferKind.Monthly,
        DefaultPackageIdentifier.Annually => ProOfferKind.Yearly,
        DefaultPackageIdentifier.Lifetime => ProOfferKind.Lifetime,
        _ => null,
    };

    private void Apply(CustomerInfoDto? info)
    {
        var entitlement = info?.Entitlements.FirstOrDefault(e => e.Identifier == EntitlementId && e.IsActive);
        var before = IsPro;
        _preferences.LastKnownPro = entitlement is not null;
        _preferences.LastKnownProExpires = entitlement?.ExpirationDate is { } expiry
            ? new DateTimeOffset(DateTime.SpecifyKind(expiry, DateTimeKind.Utc))
            : null;
        if (before != IsPro)
        {
            UiDispatch.Run(_dispatcher, () => Changed?.Invoke(this, EventArgs.Empty));
        }
    }
}
#endif
