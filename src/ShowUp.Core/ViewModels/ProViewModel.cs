using Microsoft.Extensions.Logging;
using ShowUp.Core.Entitlements;
using ShowUp.Core.Services;
using ShowUp.Services;

namespace ShowUp.Core.ViewModels;

public sealed record ProFeatureRow(string Title, string Free, string Pro);

/// <summary>An offer as the paywall shows it: the price, a one-line billing note and the screen reader name.</summary>
public sealed record ProOfferRow(ProOffer Offer, string Note, string AutomationName);

/// <summary>The paywall: what Pro adds, what it costs, and the buttons to get it.</summary>
public partial class ProViewModel : ViewModelBase
{
    private readonly IEntitlementService _entitlements;
    private readonly IStringLocalizer _localizer;
    private readonly INavigationService _navigation;
    private readonly ILauncherService _launcher;
    private readonly ProLinks _links;
    private readonly ILogger<ProViewModel> _logger;
    private bool _isSubscribed;
    private bool _isLoaded;
    private string _outcomeText = "";
    private Uri? _manageUri;

    public ProViewModel(
        IEntitlementService entitlements,
        IStringLocalizer localizer,
        INavigationService navigation,
        ILauncherService launcher,
        ProLinks links,
        ILogger<ProViewModel> logger)
    {
        _entitlements = entitlements;
        _localizer = localizer;
        _navigation = navigation;
        _launcher = launcher;
        _links = links;
        _logger = logger;
        PageTitle = localizer["ProTitle"];
        Features =
        [
            Row("ProFeaturePrograms"),
            new(_localizer["ProFeatureReminders"], _localizer["ProFeatureRemindersFree", FeatureGate.FreeReminders], _localizer["ProFeatureRemindersPro", FeatureGate.ProReminders]),
            Row("ProFeatureHistory"),
            Row("ProFeatureCustom"),
            Row("ProFeaturePersonalization"),
            Row("ProFeatureSync"),
        ];
        SubscriptionDisclosure = localizer["ProSubscriptionDisclosure"];
        IsPro = entitlements.IsPro;
        PurchasesAvailable = entitlements.PurchasesAvailable;
        UpdateStateText();
    }

    public IReadOnlyList<ProFeatureRow> Features { get; }

    public string SubscriptionDisclosure { get; }

    [ObservableProperty]
    public partial IReadOnlyList<ProOffer> Offers { get; set; } = [];

    [ObservableProperty]
    public partial IReadOnlyList<ProOfferRow> OfferRows { get; set; } = [];

    [ObservableProperty]
    public partial bool IsPro { get; set; }

    [ObservableProperty]
    public partial bool PurchasesAvailable { get; set; }

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string StateText { get; set; } = "";

    public bool CanBuy => PurchasesAvailable && !IsPro && Offers.Count > 0;

    public bool ShowSubscriptionDisclosure => CanBuy && Offers.Any(o => o.Kind != ProOfferKind.Lifetime);

    /// <summary>Offers could not be loaded although the store should have answered.</summary>
    public bool CanRetry => _isLoaded && !IsPro && PurchasesAvailable && Offers.Count == 0;

    public bool CanManageSubscription => IsPro && _manageUri is not null;

    public override void OnNavigatedTo(object? parameter)
    {
        base.OnNavigatedTo(parameter);
        _ = LoadAsync();
    }

    public override void OnNavigatedFrom()
    {
        base.OnNavigatedFrom();
        if (_isSubscribed)
        {
            _entitlements.Changed -= OnEntitlementsChanged;
            _isSubscribed = false;
        }
    }

    [RelayCommand]
    public async Task LoadAsync()
    {
        if (!_isSubscribed)
        {
            _entitlements.Changed += OnEntitlementsChanged;
            _isSubscribed = true;
        }

        IsLoading = true;
        try
        {
            await _entitlements.RefreshAsync();
            Offers = await _entitlements.GetOffersAsync();
            _manageUri = await _entitlements.GetManageSubscriptionUriAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Loading the paywall failed.");
            Offers = [];
        }
        finally
        {
            _isLoaded = true;
            IsLoading = false;
            Sync();
        }
    }

    partial void OnOffersChanged(IReadOnlyList<ProOffer> value) =>
        OfferRows = [.. value.Select(o => new ProOfferRow(o, _localizer[$"ProOffer{o.Kind}Note"], $"{o.Title}, {o.Price}"))];

    private bool CanPurchase => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanPurchase))]
    public async Task PurchaseAsync(ProOffer? offer)
    {
        if (IsBusy || offer is null)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var outcome = await _entitlements.PurchaseAsync(offer);
            _outcomeText = outcome switch
            {
                PurchaseOutcome.Purchased => _localizer["ProPurchased"],
                PurchaseOutcome.Pending => _localizer["ProPending"],
                PurchaseOutcome.Unverified => _localizer["ProCheckBack"],
                PurchaseOutcome.Unconfirmed => _localizer["ProCouldNotConfirm"],
                PurchaseOutcome.Failed => _localizer["ProFailed"],
                _ => "",
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Purchasing {Offer} failed.", offer.Id);
            _outcomeText = _localizer["ProFailed"];
        }
        finally
        {
            await RefreshManageUriAsync();
            IsBusy = false;
            Sync();
        }
    }

    private bool CanRestore => !IsBusy && PurchasesAvailable;

    [RelayCommand(CanExecute = nameof(CanRestore))]
    public async Task RestoreAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var restored = await _entitlements.RestoreAsync();
            _outcomeText = restored ? _localizer["ProRestored"] : _localizer["ProNothingToRestore"];
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Restoring purchases failed.");
            _outcomeText = _localizer["ProFailed"];
        }
        finally
        {
            await RefreshManageUriAsync();
            IsBusy = false;
            Sync();
        }
    }

    // A purchase or restore can turn a subscription into a lifetime (no link) or start one (link).
    private async Task RefreshManageUriAsync()
    {
        try
        {
            _manageUri = await _entitlements.GetManageSubscriptionUriAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Reading the manage subscription link failed.");
        }
    }

    [RelayCommand]
    private Task OpenTermsAsync() => _launcher.LaunchUriAsync(_links.Terms);

    [RelayCommand]
    private Task OpenPrivacyAsync() => _launcher.LaunchUriAsync(_links.Privacy);

    [RelayCommand]
    private async Task ManageSubscriptionAsync()
    {
        if (_manageUri is { } uri)
        {
            await _launcher.LaunchUriAsync(uri);
        }
    }

    [RelayCommand]
    private void GoBack() => _navigation.GoBack();

    partial void OnIsBusyChanged(bool value)
    {
        PurchaseCommand.NotifyCanExecuteChanged();
        RestoreCommand.NotifyCanExecuteChanged();
    }

    private void OnEntitlementsChanged(object? sender, EventArgs e) => Sync();

    private void Sync()
    {
        IsPro = _entitlements.IsPro;
        PurchasesAvailable = _entitlements.PurchasesAvailable;
        OnPropertyChanged(nameof(CanBuy));
        OnPropertyChanged(nameof(ShowSubscriptionDisclosure));
        OnPropertyChanged(nameof(CanRetry));
        OnPropertyChanged(nameof(CanManageSubscription));
        RestoreCommand.NotifyCanExecuteChanged();
        UpdateStateText();
    }

    private void UpdateStateText() =>
        StateText = IsPro
            ? _localizer["ProStateActive"]
            : !_isLoaded
                ? ""
                : !PurchasesAvailable
                    ? _localizer["ProUnavailable"]
                    : Offers.Count == 0
                        ? _localizer[_entitlements.OffersLoadFailed ? "ProOffersFailed" : "ProUnavailable"]
                        : _outcomeText;

    private ProFeatureRow Row(string key) => new(_localizer[key], _localizer[key + "Free"], _localizer[key + "Pro"]);
}
