using System.ComponentModel;
using ShowUp.Core.Navigation;
using ShowUp.Core.ViewModels;

namespace ShowUp.Views;

public partial class TodayViewBase : ViewBase<TodayViewModel> { }

[NavigationInfo(NavigationSection.Today)]
public sealed partial class TodayView : TodayViewBase
{
    private Window? _window;

    public TodayView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    /// <summary>Own width at which Today (400 px) and the calendar share the page; keeps calendar cells at 84 px or more.</summary>
    private const double SideBySideWidth = 1120;

    private bool _isWide;

    /// <summary>The calendar shown beside Today; only created once the page is wide and a program is running.</summary>
    public ProgramCalendarViewModel? CalendarViewModel { get; private set; }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var shell = FindWindowShell(XamlRoot?.Content);
        Bindings.Update();
        if (ViewModel is { } viewModel)
        {
            viewModel.PropertyChanged += ViewModel_PropertyChanged;
        }

        _isWide = RootGrid.ActualWidth >= SideBySideWidth;
        UpdateLayoutState();

        _window = shell?.ServiceProvider.GetRequiredService<IWindowShellProvider>().Window;
        if (_window is not null)
        {
            _window.Activated += Window_Activated;
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (ViewModel is { } viewModel)
        {
            viewModel.PropertyChanged -= ViewModel_PropertyChanged;
        }

        CalendarViewModel?.ViewUnloaded();
        _calendarRunning = false;
        if (_window is not null)
        {
            _window.Activated -= Window_Activated;
            _window = null;
        }
    }

    private bool _calendarRunning;

    private void RootGrid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var wide = e.NewSize.Width >= SideBySideWidth;
        if (wide != _isWide)
        {
            _isWide = wide;
            UpdateLayoutState();
        }
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TodayViewModel.HasEnrollment))
        {
            UpdateLayoutState();
        }
    }

    // Wide with a program: Today (fixed) and the calendar. Otherwise Today alone, stretching up to its cap.
    private void UpdateLayoutState()
    {
        var showCalendar = _isWide && ViewModel?.HasEnrollment == true && IsLoaded;
        if (showCalendar && CalendarViewModel is null && FindWindowShell(XamlRoot?.Content) is { } shell)
        {
            CalendarViewModel = shell.ServiceProvider.GetRequiredService<ProgramCalendarViewModel>();
            Bindings.Update();
        }

        if (showCalendar && !_calendarRunning)
        {
            _calendarRunning = true;
            CalendarViewModel?.ViewLoaded();
        }
        else if (!showCalendar && _calendarRunning)
        {
            _calendarRunning = false;
            CalendarViewModel?.ViewUnloaded();
        }

        CalendarHost.Visibility = showCalendar ? Visibility.Visible : Visibility.Collapsed;
        PanelColumn.Width = showCalendar ? new GridLength(400) : new GridLength(1, GridUnitType.Star);
        PanelColumn.MinWidth = showCalendar ? 360 : 0;
        CalendarColumn.Width = showCalendar ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        ContentPanel.MaxWidth = showCalendar ? 420 : 640;
    }

    // Coming back to the app the next morning moves today on.
    private void Window_Activated(object sender, WindowActivatedEventArgs args)
    {
        if (args.WindowActivationState == WindowActivationState.Deactivated)
        {
            return;
        }

        if (ViewModel is { } viewModel)
        {
            _ = viewModel.RefreshIfDateChangedAsync();
        }

        if (_calendarRunning && CalendarViewModel is { } calendar)
        {
            _ = calendar.RefreshIfDateChangedAsync();
        }
    }
}
