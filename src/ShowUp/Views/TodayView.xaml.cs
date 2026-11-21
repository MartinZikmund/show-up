using System.ComponentModel;
using ShowUp.Core.Navigation;
using ShowUp.Core.ViewModels;
#if DEBUG
using ShowUp.Controls;
using ShowUp.Core.Marking;
using ShowUp.Core.Tracking;
using ShowUp.Services.Localization;
#endif

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
#if DEBUG
        AddDebugMarkerTools();
#endif
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

#if DEBUG
    private int _previewCount;

    // The sample cell is a two-session day: tapping it auto-crosses the next session, drawing crosses one by hand.
    private const int SampleSessions = 2;
    private readonly HashSet<int> _sampleDone = [];
    private readonly HashSet<int> _sampleAutos = [];
    private readonly List<string> _sampleInks = [];

    private static DayStatus SampleStatus(IReadOnlySet<int> done) =>
        new(9, DateOnly.FromDateTime(DateTime.Today), SampleSessions, done, IsRestDay: false);

    private void UpdateSample(MarkerCrossControl sample)
    {
        sample.Strokes = MarkerAppearance.StrokesFor(SampleStatus(_sampleDone), _sampleAutos);
        sample.Inks = [.. _sampleInks];
    }

    private void CrossSample(MarkerCrossControl sample, CrossMomentResult result)
    {
        var session = Enumerable.Range(0, SampleSessions).First(i => !_sampleDone.Contains(i));
        _sampleDone.Add(session);
        if (result.InkJson is { } ink)
        {
            _sampleInks.Add(ink);
        }
        else
        {
            _sampleAutos.Add(session);
        }

        UpdateSample(sample);
    }

    // Debug-only playground for the marker until the calendar (Task 5) and Today card (Task 6) land.
    private void AddDebugMarkerTools()
    {
        MarkerCrossControl sample = new();
        Button cell = new()
        {
            Width = 96,
            Height = 100,
            Padding = new(0),
            BorderThickness = new(1.5),
            CornerRadius = new(4),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch,
            Content = sample,
        };
        cell.SetValue(AutomationProperties.AutomationIdProperty, "DebugMarkerCell");
        cell.SetValue(AutomationProperties.NameProperty, Localizer.Instance["DebugMarkerSample"]);
        // The cell's theme can differ from the app's, so read the merged Colors.xaml dictionary for that theme.
        cell.Loaded += (_, _) =>
        {
            var themeKey = cell.ActualTheme.ToString();
            var theme = Application.Current.Resources.MergedDictionaries
                .Where(d => d.ThemeDictionaries.ContainsKey(themeKey))
                .Select(d => (ResourceDictionary)d.ThemeDictionaries[themeKey])
                .FirstOrDefault(d => d.ContainsKey("CalendarCellBackgroundBrush"));
            if (theme is not null)
            {
                cell.Background = (Brush)theme["CalendarCellBackgroundBrush"];
                cell.BorderBrush = (Brush)theme["CalendarCellBorderBrush"];
            }
        };
        cell.Click += (_, _) =>
        {
            if (_sampleDone.Count >= SampleSessions)
            {
                _sampleDone.Clear();
                _sampleAutos.Clear();
                _sampleInks.Clear();
                sample.AnimateChanges = false;
                UpdateSample(sample);
                sample.AnimateChanges = true;
                sample.Seed++;
            }
            else
            {
                CrossSample(sample, CrossMomentResult.Auto);
            }
        };

        Button preview = new() { Content = Localizer.Instance["DebugCrossMoment"] };
        preview.SetValue(AutomationProperties.AutomationIdProperty, "DebugCrossMomentButton");
        preview.Click += async (_, _) =>
        {
            // Alternates a completed day and a half-done two-session day.
            var complete = _previewCount++ % 2 == 0;
            var crossMoment = FindWindowShell(XamlRoot?.Content)!.ServiceProvider.GetRequiredService<ICrossMoment>();
            await crossMoment.ShowAsync(new CrossMomentInfo(
                DayNumber: 10,
                TotalDays: 60,
                Title: "Cardio + Core",
                StrokesBefore: 0,
                StrokesAfter: complete ? 2 : 1,
                Seed: MarkerGeometry.SeedFrom(Guid.NewGuid()),
                SessionsDone: complete ? 2 : 1,
                SessionsRequired: 2,
                CurrentStreak: complete ? 9 : 8));
        };

        // Draws the sample day's next session; its earlier ink shows at 50%.
        Button draw = new() { Content = Localizer.Instance["DebugDrawMoment"] };
        draw.SetValue(AutomationProperties.AutomationIdProperty, "DebugDrawMomentButton");
        draw.Click += async (_, _) =>
        {
            if (_sampleDone.Count >= SampleSessions)
            {
                return;
            }

            var session = Enumerable.Range(0, SampleSessions).First(i => !_sampleDone.Contains(i));
            var (before, afterAuto, afterDrawn) = MarkerAppearance.Outcomes(SampleStatus(_sampleDone), _sampleAutos, session);
            var done = _sampleDone.Count + 1;
            var crossMoment = FindWindowShell(XamlRoot?.Content)!.ServiceProvider.GetRequiredService<ICrossMoment>();
            var result = await crossMoment.DrawAsync(new CrossMomentDrawRequest(
                new CrossMomentInfo(
                    DayNumber: 10,
                    TotalDays: 60,
                    Title: "Cardio + Core",
                    StrokesBefore: before,
                    StrokesAfter: afterAuto,
                    Seed: sample.Seed,
                    SessionsDone: done,
                    SessionsRequired: SampleSessions,
                    CurrentStreak: done >= SampleSessions ? 9 : 8),
                [.. _sampleInks],
                afterDrawn));
            if (result.Crossed)
            {
                sample.AnimateChanges = false;
                CrossSample(sample, result);
                sample.AnimateChanges = true;
            }
        };

        ToggleSwitch slowMotion = new() { Header = Localizer.Instance["DebugSlowMotion"] };
        slowMotion.SetValue(AutomationProperties.AutomationIdProperty, "DebugSlowMotionToggle");
        slowMotion.Toggled += (_, _) => MarkerCrossControl.DebugTimeScale = slowMotion.IsOn ? 25 : 1;

        DebugPanel.Children.Add(cell);
        DebugPanel.Children.Add(preview);
        DebugPanel.Children.Add(draw);
        DebugPanel.Children.Add(slowMotion);
    }
#endif
}
