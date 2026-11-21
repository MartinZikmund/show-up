using ShowUp.Core.Entitlements;
using ShowUp.Core.Infrastructure;
using CommunityToolkit.Mvvm.Messaging;
using ShowUp.Core.Marking;
using ShowUp.Core.Messages;
using ShowUp.Core.Services;
using ShowUp.Core.Tracking;
using ShowUp.Services.Settings;
using ShowUp.Services.Theming;
using MZikmund.Toolkit.WinUI.Services;

namespace ShowUp.Core.ViewModels;

public partial class SettingsViewModel : ViewModelBase
{
    private readonly IStringLocalizer _localizer;
    private readonly IAppPreferences _appPreferences;
    private readonly IThemeManager _themeManager;
    private readonly IPreferences _preferences;
    private readonly IApplication _application;
    private readonly IMessenger _messenger;
    private readonly IEntitlementService _entitlements;
    private readonly INavigationService _navigation;
    private bool _isInitializing;
    private bool _isSubscribed;

    public SettingsViewModel(
        IStringLocalizer localizer,
        IAppPreferences appPreferences,
        IThemeManager themeManager,
        IPreferences preferences,
        IApplication application,
        IMessenger messenger,
        IEntitlementService entitlements,
        INavigationService navigation,
        AccountViewModel account)
    {
        Account = account;
        _entitlements = entitlements;
        _navigation = navigation;
        _messenger = messenger;
        _localizer = localizer;
        _appPreferences = appPreferences;
        _themeManager = themeManager;
        _preferences = preferences;
        _application = application;
        PageTitle = _localizer["Settings"];
        MarkerSwatches = [.. Enum.GetValues<MarkerColor>().Select(c => new MarkerSwatch(c, _localizer[$"MarkerColor_{c}"]))];
    }

    /// <summary>The account card.</summary>
    public AccountViewModel Account { get; }

    public override void OnNavigatedTo(object? parameter)
    {
        base.OnNavigatedTo(parameter);
        Account.Activate();
        if (!_isSubscribed)
        {
            _entitlements.Changed += OnEntitlementsChanged;
            _messenger.Register<SettingsViewModel, PersonalizationReloadedMessage>(this, static (r, _) => r.LoadValues());
            _isSubscribed = true;
        }

        OnEntitlementsChanged(null, EventArgs.Empty);
        LoadValues();
    }

    // Also after a sign-in or sync brought another account's personalization in.
    private void LoadValues()
    {
        try
        {
            _isInitializing = true;
            Theme = _appPreferences.Theme;
            MarkerSoundEnabled = _appPreferences.MarkerSoundEnabled;
            MarkerHapticsEnabled = _appPreferences.MarkerHapticsEnabled;
            CrossMomentMode = _appPreferences.CrossMomentMode;
            CrossOffStyle = _appPreferences.CrossOffStyle;
            MarkerColor = _appPreferences.MarkerColor;
            UnitSystem = _appPreferences.UnitSystem;
            SyncSwatches();
        }
        finally
        {
            _isInitializing = false;
        }
    }

    public override void OnNavigatedFrom()
    {
        base.OnNavigatedFrom();
        Account.Deactivate();
        if (_isSubscribed)
        {
            _entitlements.Changed -= OnEntitlementsChanged;
            _messenger.Unregister<PersonalizationReloadedMessage>(this);
            _isSubscribed = false;
        }
    }

    private void OnEntitlementsChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(CanChangeMarkerColor));
        OnPropertyChanged(nameof(IsMarkerColorLocked));
        OnPropertyChanged(nameof(ProStatusText));
        SyncSwatches();
        OnPropertyChanged(nameof(SelectedMarkerSwatch));
    }

    /// <summary>Status line of the "Show Up Pro" card.</summary>
    public string ProStatusText => _localizer[_entitlements.IsPro ? "ProCardStatusActive" : "ProCardStatusFree"];

    [RelayCommand]
    private void OpenPro() => _navigation.Navigate<ProViewModel>();

    [RelayCommand]
    private void OpenReminders() => _navigation.Navigate<RemindersViewModel>();

    public ElementTheme[] ThemeOptions { get; } = [ElementTheme.Default, ElementTheme.Light, ElementTheme.Dark];

    [ObservableProperty]
    public partial ElementTheme Theme { get; set; }

    partial void OnThemeChanged(ElementTheme value)
    {
        if (_isInitializing)
        {
            return;
        }

        _themeManager.SetTheme(value);
        _appPreferences.Theme = value;
    }

    [ObservableProperty]
    public partial bool MarkerSoundEnabled { get; set; }

    partial void OnMarkerSoundEnabledChanged(bool value)
    {
        if (!_isInitializing)
        {
            _appPreferences.MarkerSoundEnabled = value;
        }
    }

    [ObservableProperty]
    public partial bool MarkerHapticsEnabled { get; set; }

    partial void OnMarkerHapticsEnabledChanged(bool value)
    {
        if (!_isInitializing)
        {
            _appPreferences.MarkerHapticsEnabled = value;
        }
    }

    public CrossMomentMode[] CrossMomentModeOptions { get; } = [CrossMomentMode.OnlyToday, CrossMomentMode.Always, CrossMomentMode.Never];

    [ObservableProperty]
    public partial CrossMomentMode CrossMomentMode { get; set; }

    partial void OnCrossMomentModeChanged(CrossMomentMode value)
    {
        if (!_isInitializing)
        {
            _appPreferences.CrossMomentMode = value;
        }
    }

    public UnitSystem[] UnitSystemOptions { get; } = [UnitSystem.Metric, UnitSystem.Imperial];

    [ObservableProperty]
    public partial UnitSystem UnitSystem { get; set; }

    partial void OnUnitSystemChanged(UnitSystem value)
    {
        if (!_isInitializing)
        {
            _appPreferences.UnitSystem = value;
        }
    }

    public CrossOffStyle[] CrossOffStyleOptions { get; } = [CrossOffStyle.DrawYourself, CrossOffStyle.AutoMarker];

    [ObservableProperty]
    public partial CrossOffStyle CrossOffStyle { get; set; }

    partial void OnCrossOffStyleChanged(CrossOffStyle value)
    {
        if (!_isInitializing)
        {
            _appPreferences.CrossOffStyle = value;
        }
    }

    public MarkerSwatch[] MarkerSwatches { get; }

    // The property's default (Auto) raises no change, so the swatches need an explicit first sync.
    private void SyncSwatches()
    {
        foreach (var swatch in MarkerSwatches)
        {
            swatch.IsSelected = swatch.Color == ShownMarkerColor;
        }
    }

    public bool CanChangeMarkerColor => FeatureGate.CanUse(ProFeature.Personalization, _entitlements.IsPro);

    /// <summary>Shows the "Unlock with Pro" hint under the picker.</summary>
    public bool IsMarkerColorLocked => !CanChangeMarkerColor;

    [ObservableProperty]
    public partial MarkerColor MarkerColor { get; set; }

    partial void OnMarkerColorChanged(MarkerColor value)
    {
        SyncSwatches();
        OnPropertyChanged(nameof(SelectedMarkerSwatch));
        if (_isInitializing)
        {
            return;
        }

        _appPreferences.MarkerColor = value;
        _messenger.Send(new MarkerColorChangedMessage(value));
    }

    /// <summary>The ink actually drawn: a lapsed user's stored choice is kept but shows as Auto.</summary>
    public MarkerColor ShownMarkerColor => FeatureGate.EffectiveMarkerColor(MarkerColor, _entitlements.IsPro);

    /// <summary>Two-way target for the picker; null while the selection is being cleared.</summary>
    public MarkerSwatch? SelectedMarkerSwatch
    {
        get => MarkerSwatches.FirstOrDefault(s => s.Color == ShownMarkerColor);
        set
        {
            if (value is not null && CanChangeMarkerColor)
            {
                MarkerColor = value.Color;
            }
        }
    }

    public string AppVersion => _application.AppVersion;

    /// <summary>
    /// Localized "Worktree: {name}" line shown under the version, or <see langword="null"/> when
    /// this build did not come from a git worktree. Composed here rather than in XAML because
    /// {markup:Localize} takes no format arguments.
    /// </summary>
    public string? WorktreeLabel =>
        _application.WorktreeName is { Length: > 0 } worktree
            ? _localizer["WorktreeFormat", worktree].Value
            : null;

    public bool IsDebug =>
#if DEBUG
        true;
#else
        false;
#endif

    [RelayCommand]
    private void ClearPreferences()
    {
        _preferences.Clear();
    }
}
