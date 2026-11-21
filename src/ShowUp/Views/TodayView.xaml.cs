using ShowUp.Core.Navigation;
using ShowUp.Core.ViewModels;
#if DEBUG
using ShowUp.Controls;
using ShowUp.Core.Marking;
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
            if (sample.Strokes >= 2 || sample.Inks is { Count: > 0 })
            {
                sample.Strokes = 0;
                sample.Inks = null;
                sample.Seed++;
            }
            else
            {
                sample.Strokes++;
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

        // Draw it yourself on a two-session day: the second draw shows the first one's ink at 50%.
        Button draw = new() { Content = Localizer.Instance["DebugDrawMoment"] };
        draw.SetValue(AutomationProperties.AutomationIdProperty, "DebugDrawMomentButton");
        draw.Click += async (_, _) =>
        {
            var inks = sample.Inks ?? [];
            var done = Math.Min(2, inks.Count + (sample.Strokes >= 2 ? 2 : 0) + 1);
            var crossMoment = FindWindowShell(XamlRoot?.Content)!.ServiceProvider.GetRequiredService<ICrossMoment>();
            var result = await crossMoment.DrawAsync(new CrossMomentDrawRequest(
                new CrossMomentInfo(
                    DayNumber: 10,
                    TotalDays: 60,
                    Title: "Cardio + Core",
                    StrokesBefore: sample.Strokes,
                    StrokesAfter: 2,
                    Seed: sample.Seed,
                    SessionsDone: done,
                    SessionsRequired: 2,
                    CurrentStreak: done >= 2 ? 9 : 8),
                inks));
            if (result is { Outcome: CrossMomentOutcome.Drawn, InkJson: { } ink })
            {
                sample.Inks = [.. inks, ink];
            }
            else if (result.Outcome == CrossMomentOutcome.Auto)
            {
                sample.Strokes = 2;
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
