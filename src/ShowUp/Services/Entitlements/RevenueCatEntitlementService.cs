#if __ANDROID__ || __IOS__
using Microsoft.Extensions.Logging;
using ShowUp.Core.Entitlements;
using ShowUp.Services.Settings;
using Uno.RevenueCat.Enums;
using Uno.RevenueCat.Extensions;
using Uno.RevenueCat.Models;
using Uno.RevenueCat.Services;

namespace ShowUp.Services.Entitlements;

/// <summary>Android and iOS Pro through RevenueCat; the last known state is cached so offline launches stay Pro.</summary>
public sealed class RevenueCatEntitlementService : IEntitlementService
{
    private const string EntitlementId = "pro";

    private readonly IRevenueCatBilling _billing;
    private readonly IAppPreferences _preferences;
    private readonly IStringLocalizer _localizer;
    private readonly ILogger<RevenueCatEntitlementService> _logger;
    private readonly string _apiKey;
    private readonly Dictionary<string, PackageDto> _packages = [];
    private bool _initialized;

    public RevenueCatEntitlementService(
        IRevenueCatBilling billing,
        IAppPreferences preferences,
        IStringLocalizer localizer,
        ILogger<RevenueCatEntitlementService> logger,
        string apiKey)
    {
        _billing = billing;
        _preferences = preferences;
        _localizer = localizer;
        _logger = logger;
        _apiKey = apiKey;
        IsPro = preferences.LastKnownPro;
    }

    public bool IsPro { get; private set; }

    public bool PurchasesAvailable => _apiKey.Length > 0;

    public event EventHandler? Changed;

    public async Task RefreshAsync()
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
                SetPro(HasPro(result.Value));
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Refreshing the Pro entitlement failed.");
        }
    }

    public async Task<IReadOnlyList<ProOffer>> GetOffersAsync()
    {
        if (!EnsureInitialized())
        {
            return [];
        }

        try
        {
            var result = await _billing.GetOfferingsAsync();
            if (!result.IsSuccess)
            {
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
                SetPro(HasPro(result.Value));
                return IsPro ? PurchaseOutcome.Purchased : PurchaseOutcome.Failed;
            }

            return result.Error switch
            {
                PurchaseErrorStatus.PurchaseCancelledError => PurchaseOutcome.Cancelled,
                PurchaseErrorStatus.PaymentPendingError => PurchaseOutcome.Pending,
                _ => PurchaseOutcome.Failed,
            };
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
                SetPro(HasPro(result.Value));
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Restoring purchases failed.");
        }

        return IsPro;
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

    private static bool HasPro(CustomerInfoDto? info) =>
        info?.Entitlements.Any(e => e.Identifier == EntitlementId && e.IsActive) == true;

    private static ProOfferKind? KindOf(string identifier) => identifier switch
    {
        DefaultPackageIdentifier.Monthly => ProOfferKind.Monthly,
        DefaultPackageIdentifier.Annually => ProOfferKind.Yearly,
        DefaultPackageIdentifier.Lifetime => ProOfferKind.Lifetime,
        _ => null,
    };

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
