using ShowUp.Core.Navigation;
using ShowUp.Core.Services;

namespace ShowUp.Core.ViewModels;

public partial class WindowShellViewModel : ObservableObject
{
    private readonly INavigationService _navigationService;
    private readonly IStringLocalizer _localizer;

    public WindowShellViewModel(INavigationService navigationService, IStringLocalizer localizer)
    {
        _navigationService = navigationService ?? throw new ArgumentNullException(nameof(navigationService));
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
    }

    [ObservableProperty]
    public partial string Title { get; set; } = "";

    [ObservableProperty]
    public partial NavigationSection SelectedSection { get; set; } = NavigationSection.Today;

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    public partial string LoadingStatusMessage { get; set; } = "";

    public bool CanGoBack => _navigationService.CanGoBack;

    public void NotifyCanGoBackChanged() => OnPropertyChanged(nameof(CanGoBack));

    [RelayCommand]
    public void NavigateToSection(NavigationSection section)
    {
        if (_navigationService.CurrentSection == section)
        {
            return;
        }

        switch (section)
        {
            case NavigationSection.Today:
                _navigationService.Navigate<TodayViewModel>();
                break;
            case NavigationSection.Calendar:
                _navigationService.Navigate<ProgramCalendarViewModel>();
                break;
            case NavigationSection.Programs:
                _navigationService.Navigate<ProgramsViewModel>();
                break;
            case NavigationSection.History:
                _navigationService.Navigate<HistoryViewModel>();
                break;
            case NavigationSection.Settings:
                _navigationService.Navigate<SettingsViewModel>();
                break;
            default:
                throw new NotSupportedException($"Navigation section not supported: {section}");
        }

        // Sections are top-level destinations; switching between them shouldn't build a back stack.
        _navigationService.ClearBackStack();
        Select(section);

        // Frame.Navigated (and its CanGoBack sync) fired before the back stack was cleared.
        NotifyCanGoBackChanged();
    }

    /// <summary>Re-reads the current section after a navigation the shell didn't start (e.g. back).</summary>
    public void SyncWithNavigation()
    {
        if (_navigationService.CurrentSection is { } section)
        {
            Select(section);
        }

        NotifyCanGoBackChanged();
    }

    private void Select(NavigationSection section)
    {
        SelectedSection = section;
        // Resource keys match the section names.
        Title = _localizer[section.ToString()];
    }

    [RelayCommand]
    public void GoBack()
    {
        _navigationService.GoBack();
        NotifyCanGoBackChanged();
    }
}
