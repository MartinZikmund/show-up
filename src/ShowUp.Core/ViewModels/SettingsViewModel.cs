using ShowUp.Core.Infrastructure;
using CommunityToolkit.Mvvm.Messaging;
using ShowUp.Core.Marking;
using ShowUp.Core.Messages;
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
    private bool _isInitializing;

    public SettingsViewModel(
        IStringLocalizer localizer,
        IAppPreferences appPreferences,
        IThemeManager themeManager,
        IPreferences preferences,
        IApplication application,
        IMessenger messenger)
    {
        _messenger = messenger;
        _localizer = localizer;
        _appPreferences = appPreferences;
        _themeManager = themeManager;
        _preferences = preferences;
        _application = application;
        PageTitle = _localizer["Settings"];
        MarkerSwatches = [.. Enum.GetValues<MarkerColor>().Select(c => new MarkerSwatch(c, _localizer[$"MarkerColor_{c}"]))];
    }

    public override void OnNavigatedTo(object? parameter)
    {
        base.OnNavigatedTo(parameter);
        try
        {
            _isInitializing = true;
            Theme = _appPreferences.Theme;
            MarkerSoundEnabled = _appPreferences.MarkerSoundEnabled;
            MarkerHapticsEnabled = _appPreferences.MarkerHapticsEnabled;
            CrossMomentMode = _appPreferences.CrossMomentMode;
            CrossOffStyle = _appPreferences.CrossOffStyle;
            MarkerColor = _appPreferences.MarkerColor;
        }
        finally
        {
            _isInitializing = false;
        }
    }

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

    /// <summary>Whether the marker color picker is unlocked; Plan 3 wires this to the Personalization feature gate.</summary>
    public bool CanChangeMarkerColor => true;

    [ObservableProperty]
    public partial MarkerColor MarkerColor { get; set; }

    partial void OnMarkerColorChanged(MarkerColor value)
    {
        foreach (var swatch in MarkerSwatches)
        {
            swatch.IsSelected = swatch.Color == value;
        }

        if (_isInitializing)
        {
            return;
        }

        _appPreferences.MarkerColor = value;
        _messenger.Send(new MarkerColorChangedMessage(value));
    }

    /// <summary>Two-way target for the picker; null while the selection is being cleared.</summary>
    public MarkerSwatch? SelectedMarkerSwatch
    {
        get => MarkerSwatches.FirstOrDefault(s => s.Color == MarkerColor);
        set
        {
            if (value is not null && CanChangeMarkerColor)
            {
                MarkerColor = value.Color;
                OnPropertyChanged();
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
