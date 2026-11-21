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
    public TodayView()
    {
        InitializeComponent();
#if DEBUG
        AddDebugMarkerTools();
#endif
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

        ContentPanel.Children.Add(cell);
        ContentPanel.Children.Add(preview);
        ContentPanel.Children.Add(draw);
        ContentPanel.Children.Add(slowMotion);
    }
#endif
}
