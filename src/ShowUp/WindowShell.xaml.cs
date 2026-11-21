using CommunityToolkit.Mvvm.Messaging;
using ShowUp.Core.Marking;
using ShowUp.Core.Messages;
using ShowUp.Core.Navigation;
using ShowUp.Core.Reminders;
using ShowUp.Core.Services;
using ShowUp.Core.Tracking;
using ShowUp.Core.ViewModels;
using ShowUp.Infrastructure;
using ShowUp.Services.Navigation;
using ShowUp.Services.Settings;
using ShowUp.Services.Theming;
using Microsoft.UI.Windowing;

namespace ShowUp;

public sealed partial class WindowShell : Page, IWindowShell
{
    private readonly IServiceScope _windowScope;
    private readonly Window _associatedWindow;
    private bool _isWindowClosed;
    private IDisposable? _lapseWatch;
    private InAppReminderScheduler? _inAppReminders;

    public WindowShell(IServiceProvider serviceProvider, Window associatedWindow)
    {
        InitializeComponent();
        CrossMomentOverlay.ShellContent = ShellContent;

        _windowScope = serviceProvider.CreateScope();
        var windowShellProvider = (WindowShellProvider)ServiceProvider.GetRequiredService<IWindowShellProvider>();
        windowShellProvider.SetShell(this, associatedWindow);

        var navigationService = ServiceProvider.GetRequiredService<INavigationService>();
        navigationService.Initialize();

        // Restore saved theme
        var settings = ServiceProvider.GetRequiredService<IAppPreferences>();
        var themeManager = ServiceProvider.GetRequiredService<IThemeManager>();
        themeManager.SetTheme(settings.Theme);

        _associatedWindow = associatedWindow;
        _associatedWindow.Closed += OnWindowClosed;
        CustomizeWindow();

        ViewModel = ServiceProvider.GetRequiredService<WindowShellViewModel>();
        ViewModel.PropertyChanged += ViewModel_PropertyChanged;

        InnerFrame.Navigated += InnerFrame_Navigated;
        Loading += WindowShell_Loading;
        Loaded += (_, _) =>
        {
            UpdateStatusBarForeground();
            UpdateTitleBarPaneToggle(WidthStates.CurrentState);
            WatchForLapse();
            WatchReminders();
        };
        ActualThemeChanged += (_, _) => UpdateStatusBarForeground();

        UpdateWindowTitle();
    }

    public IServiceProvider ServiceProvider => _windowScope.ServiceProvider;

    // Pro lapsing with several programs running asks once (app-wide) which one stays primary.
    private void WatchForLapse()
    {
        if (_lapseWatch is not null)
        {
            return;
        }

        _lapseWatch = ServiceProvider.GetRequiredService<LapseCoordinator>().Attach(ServiceProvider.GetRequiredService<IPrimaryProgramPrompt>());
    }

    // A tapped notification opens Today; heads without OS notifications get a banner instead.
    private void WatchReminders()
    {
        var messenger = ServiceProvider.GetRequiredService<IMessenger>();
        if (messenger.IsRegistered<ReminderOpenedMessage>(this))
        {
            return;
        }

        messenger.Register<WindowShell, ReminderOpenedMessage>(this, static (shell, _) => shell.DispatcherQueue.TryEnqueue(shell.OpenToday));
        if (ServiceProvider.GetRequiredService<IReminderScheduler>() is InAppReminderScheduler inApp)
        {
            _inAppReminders = inApp;
            inApp.Due += OnInAppReminderDue;
        }
    }

    private void OnInAppReminderDue(object? sender, PlannedReminder reminder) => DispatcherQueue.TryEnqueue(() =>
    {
        if (_isWindowClosed)
        {
            return;
        }

        ReminderBanner.Title = reminder.Title;
        ReminderBanner.Message = reminder.Body;
        ReminderBannerHost.Visibility = Visibility.Visible;
        ReminderBanner.IsOpen = true;
        ServiceProvider.GetRequiredService<IMarkingFeedback>().DayTaken();
    });

    private void OpenToday()
    {
        if (_isWindowClosed)
        {
            return;
        }

        ViewModel.NavigateToSection(NavigationSection.Today);
        _associatedWindow.Activate();
    }

    private void ReminderBanner_Closed(InfoBar sender, InfoBarClosedEventArgs args) => ReminderBannerHost.Visibility = Visibility.Collapsed;

    private void ReminderBannerOpen_Click(object sender, RoutedEventArgs e)
    {
        ReminderBanner.IsOpen = false;
        OpenToday();
    }

    public WindowShellViewModel ViewModel { get; }

    public Frame RootFrame => InnerFrame;

    public ICrossMoment CrossMoment => CrossMomentOverlay;

    private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(WindowShellViewModel.Title))
        {
            UpdateWindowTitle();
        }
        else if (e.PropertyName == nameof(WindowShellViewModel.SelectedSection))
        {
            UpdateNavigationViewSelection();
        }
    }

    private void UpdateWindowTitle()
    {
        if (_isWindowClosed)
        {
            return;
        }

        var title = ServiceProvider.GetRequiredService<IStringLocalizer>()["ApplicationName"].Value;
        if (ViewModel.Title is { Length: > 0 } section)
        {
            title = $"{section} · {title}";
        }

        // Worktree builds carry their own package identity; showing the worktree here is what
        // makes two simultaneously-running builds tellable apart in the taskbar and Alt-Tab.
        _associatedWindow.Title = AppEnvironment.WorktreeName is { Length: > 0 } worktree ? $"{title} — {worktree}" : title;
    }

    private void InnerFrame_Navigated(object sender, Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        ViewModel.SyncWithNavigation();
        UpdateNavigationViewSelection();
    }

    private void OnWindowClosed(object sender, WindowEventArgs args)
    {
        _isWindowClosed = true;
        _lapseWatch?.Dispose();
        ServiceProvider.GetRequiredService<IMessenger>().UnregisterAll(this);
        if (_inAppReminders is not null)
        {
            _inAppReminders.Due -= OnInAppReminderDue;
        }
        _associatedWindow.Closed -= OnWindowClosed;
        InnerFrame.Navigated -= InnerFrame_Navigated;
        ViewModel.PropertyChanged -= ViewModel_PropertyChanged;
    }

    // The Android theme asks for dark status bar icons, which vanish against the dark theme.
    private void UpdateStatusBarForeground()
    {
#if __ANDROID__
        Windows.UI.ViewManagement.StatusBar.GetForCurrentView().ForegroundColor =
            ActualTheme == ElementTheme.Dark ? Microsoft.UI.Colors.White : Microsoft.UI.Colors.Black;
#endif
    }

    private void WindowShell_Loading(FrameworkElement sender, object args)
    {
        var windowShellProvider = (WindowShellProvider)ServiceProvider.GetRequiredService<IWindowShellProvider>();
        windowShellProvider.SetXamlRoot(XamlRoot ?? throw new InvalidOperationException("XamlRoot must be set."));
    }

    public void SetTitleBar(UIElement? titleBar)
    {
        if (_isWindowClosed)
        {
            return;
        }

#if !HAS_UNO
        _associatedWindow.SetTitleBar(titleBar ?? AppTitleBar);
#else
        _associatedWindow.SetTitleBar(titleBar);
#endif
    }

    private void CustomizeWindow()
    {
#if !HAS_UNO
        if (AppWindowTitleBar.IsCustomizationSupported())
        {
            _associatedWindow.ExtendsContentIntoTitleBar = true;
            _associatedWindow.AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
            _associatedWindow.SetTitleBar(AppTitleBar);
        }
#endif
    }

    #region Navigation

    private void NavView_Loaded(object sender, RoutedEventArgs e)
        => ViewModel.NavigateToSection(NavigationSection.Today);

    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.IsSettingsSelected)
        {
            ViewModel.NavigateToSection(NavigationSection.Settings);
        }
        else if (args.SelectedItemContainer is NavigationViewItem { Tag: string tag }
            && Enum.TryParse<NavigationSection>(tag, out var section))
        {
            ViewModel.NavigateToSection(section);
        }
    }

    private void NavView_BackRequested(NavigationView sender, NavigationViewBackRequestedEventArgs args)
    {
        var nav = ServiceProvider.GetRequiredService<INavigationService>();
        nav.GoBack();
    }

#if !HAS_UNO
    private void AppTitleBar_BackRequested(TitleBar sender, object args)
    {
        var nav = ServiceProvider.GetRequiredService<INavigationService>();
        nav.GoBack();
    }

    private void AppTitleBar_PaneToggleRequested(TitleBar sender, object args)
        => NavView.IsPaneOpen = !NavView.IsPaneOpen;
#endif

    // The TitleBar only exists on Windows, so its pane toggle follows the width state from code.
    private void WidthStates_CurrentStateChanged(object sender, VisualStateChangedEventArgs e)
        => UpdateTitleBarPaneToggle(e.NewState);

    private void UpdateTitleBarPaneToggle(VisualState? widthState)
    {
#if !HAS_UNO
        AppTitleBar.IsPaneToggleButtonVisible = widthState != Narrow;
#endif
    }

    private void UpdateNavigationViewSelection()
    {
        object? item = ViewModel.SelectedSection switch
        {
            NavigationSection.Today => TodayNavItem,
            NavigationSection.Calendar => CalendarNavItem,
            NavigationSection.Programs => ProgramsNavItem,
            NavigationSection.History => HistoryNavItem,
            NavigationSection.Settings => NavView.SettingsItem,
            _ => null,
        };

        if (item is not null && !ReferenceEquals(NavView.SelectedItem, item))
        {
            NavView.SelectedItem = item;
        }
    }

    #endregion
}
