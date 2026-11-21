using ShowUp.Core.Entitlements;
using ShowUp.Core.Services;

namespace ShowUp.Core.ViewModels;

public sealed record ProFeatureRow(string Title, string Free, string Pro);

/// <summary>The paywall: what Pro adds, what it costs, and the buttons to get it.</summary>
public partial class ProViewModel : ViewModelBase
{
    private readonly IEntitlementService _entitlements;
    private readonly IStringLocalizer _localizer;
    private readonly INavigationService _navigation;
    private bool _isSubscribed;
    private bool _isLoaded;
    private string _outcomeText = "";

    public ProViewModel(IEntitlementService entitlements, IStringLocalizer localizer, INavigationService navigation)
    {
        _entitlements = entitlements;
        _localizer = localizer;
        _navigation = navigation;
        PageTitle = localizer["ProTitle"];
        Features =
        [
            Row("ProFeaturePrograms"),
            Row("ProFeatureReminders"),
            Row("ProFeatureHistory"),
            Row("ProFeatureCustom"),
            Row("ProFeaturePersonalization"),
            Row("ProFeatureSync"),
        ];
        IsPro = entitlements.IsPro;
        PurchasesAvailable = entitlements.PurchasesAvailable;
        UpdateStateText();
    }

    public IReadOnlyList<ProFeatureRow> Features { get; }

    [ObservableProperty]
    public partial IReadOnlyList<ProOffer> Offers { get; set; } = [];

    [ObservableProperty]
    public partial bool IsPro { get; set; }

    [ObservableProperty]
    public partial bool PurchasesAvailable { get; set; }

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string StateText { get; set; } = "";

    public bool CanBuy => PurchasesAvailable && !IsPro && Offers.Count > 0;

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
        }
        catch
        {
            Offers = [];
        }
        finally
        {
            _isLoaded = true;
            IsLoading = false;
            Sync();
        }
    }

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
                PurchaseOutcome.Failed => _localizer["ProFailed"],
                _ => "",
            };
        }
        catch
        {
            _outcomeText = _localizer["ProFailed"];
        }
        finally
        {
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
        catch
        {
            _outcomeText = _localizer["ProFailed"];
        }
        finally
        {
            IsBusy = false;
            Sync();
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
        RestoreCommand.NotifyCanExecuteChanged();
        UpdateStateText();
    }

    private void UpdateStateText() =>
        StateText = IsPro
            ? _localizer["ProStateActive"]
            : !_isLoaded
                ? ""
                : !PurchasesAvailable || Offers.Count == 0
                    ? _localizer["ProUnavailable"]
                    : _outcomeText;

    private ProFeatureRow Row(string key) => new(_localizer[key], _localizer[key + "Free"], _localizer[key + "Pro"]);
}
