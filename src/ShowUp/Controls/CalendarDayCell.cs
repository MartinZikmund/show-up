using System.ComponentModel;
using System.Globalization;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Shapes;
using ShowUp.Core.ViewModels;
using ShowUp.Services.Localization;

namespace ShowUp.Controls;

public enum CalendarCellAction
{
    Cross,
    Uncross,
    LogBonus,
}

/// <summary>
/// One day of the paper calendar. Code-driven rather than x:Bind so a recycled cell lands its marks instantly,
/// and only a change to the same day animates.
/// </summary>
[TemplatePart(Name = "PART_Content", Type = typeof(Grid))]
[TemplatePart(Name = "PART_Shake", Type = typeof(TranslateTransform))]
[TemplatePart(Name = "PART_DayNumber", Type = typeof(TextBlock))]
[TemplatePart(Name = "PART_Date", Type = typeof(TextBlock))]
[TemplatePart(Name = "PART_Title", Type = typeof(TextBlock))]
[TemplatePart(Name = "PART_Ticks", Type = typeof(StackPanel))]
[TemplatePart(Name = "PART_Bonus", Type = typeof(Border))]
[TemplatePart(Name = "PART_BonusText", Type = typeof(TextBlock))]
[TemplatePart(Name = "PART_Cross", Type = typeof(MarkerCrossControl))]
[TemplatePart(Name = "PART_Today", Type = typeof(Border))]
public sealed partial class CalendarDayCell : Button
{
    /// <summary>Below this width a cell shows only the day number and session ticks.</summary>
    private const double CompactWidth = 64;

    public static readonly DependencyProperty DayProperty = DependencyProperty.Register(
        nameof(Day), typeof(CalendarDayViewModel), typeof(CalendarDayCell),
        new PropertyMetadata(null, (d, e) => ((CalendarDayCell)d).OnDayChanged((CalendarDayViewModel?)e.OldValue, (CalendarDayViewModel?)e.NewValue)));

    private readonly MenuFlyoutItem _uncrossItem;
    private readonly MenuFlyoutItem _bonusItem;
    private readonly MenuFlyout _completeFlyout;
    private Grid? _content;
    private TranslateTransform? _shake;
    private TextBlock? _dayNumber;
    private TextBlock? _date;
    private TextBlock? _title;
    private StackPanel? _ticks;
    private Border? _bonus;
    private TextBlock? _bonusText;
    private MarkerCrossControl? _cross;
    private Border? _today;
    private Storyboard? _shakeStoryboard;
    private bool _isCompact;

    public CalendarDayCell()
    {
        _uncrossItem = MenuItem("CalendarUncross", "CalendarUncrossItem", "", CalendarCellAction.Uncross);
        _bonusItem = MenuItem("CalendarLogBonus", "CalendarLogBonusItem", "", CalendarCellAction.LogBonus);
        MenuFlyout context = new() { Items = { _uncrossItem, _bonusItem } };
        context.Opening += (_, _) =>
        {
            _uncrossItem.IsEnabled = Day?.Sessions.Any(s => s.IsDone) == true;
            _bonusItem.IsEnabled = Day is { IsFuture: false };
        };
        ContextFlyout = context;

        _completeFlyout = new() { Items = { MenuItem("CalendarUncrossLast", "CalendarUncrossLastItem", "", CalendarCellAction.Uncross) } };

        Click += (_, _) =>
        {
            if (Day is { CanCross: false })
            {
                _completeFlyout.ShowAt(this);
            }
            else
            {
                ActionRequested?.Invoke(this, CalendarCellAction.Cross);
            }
        };
        SizeChanged += (_, e) => UpdateCompact(e.NewSize.Width);
        ActualThemeChanged += (_, _) => UpdateTicks();
    }

    public event TypedEventHandler<CalendarDayCell, CalendarCellAction>? ActionRequested;

    public CalendarDayViewModel? Day
    {
        get => (CalendarDayViewModel?)GetValue(DayProperty);
        set => SetValue(DayProperty, value);
    }

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _content = GetTemplateChild("PART_Content") as Grid;
        _shake = GetTemplateChild("PART_Shake") as TranslateTransform;
        _dayNumber = GetTemplateChild("PART_DayNumber") as TextBlock;
        _date = GetTemplateChild("PART_Date") as TextBlock;
        _title = GetTemplateChild("PART_Title") as TextBlock;
        _ticks = GetTemplateChild("PART_Ticks") as StackPanel;
        _bonus = GetTemplateChild("PART_Bonus") as Border;
        _bonusText = GetTemplateChild("PART_BonusText") as TextBlock;
        _cross = GetTemplateChild("PART_Cross") as MarkerCrossControl;
        _today = GetTemplateChild("PART_Today") as Border;
        ApplyAll();
    }

    private MenuFlyoutItem MenuItem(string textKey, string automationId, string glyph, CalendarCellAction action)
    {
        MenuFlyoutItem item = new() { Text = Localizer.Instance[textKey], Icon = new FontIcon { Glyph = glyph } };
        AutomationProperties.SetAutomationId(item, automationId);
        item.Click += (_, _) => ActionRequested?.Invoke(this, action);
        return item;
    }

    private void OnDayChanged(CalendarDayViewModel? oldDay, CalendarDayViewModel? newDay)
    {
        if (oldDay is not null)
        {
            oldDay.PropertyChanged -= Day_PropertyChanged;
        }

        if (newDay is not null)
        {
            newDay.PropertyChanged += Day_PropertyChanged;
        }

        StopShake();
        ApplyAll();
    }

    private void Day_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender != Day)
        {
            return;
        }

        switch (e.PropertyName)
        {
            case nameof(CalendarDayViewModel.MarkStrokes):
                ApplyStrokes(animate: Day!.AnimateMarks);
                break;
            case nameof(CalendarDayViewModel.ShakeRequested) when Day!.ShakeRequested:
                Shake();
                break;
            case nameof(CalendarDayViewModel.ShakeRequested):
                break;
            default:
                ApplyText();
                break;
        }
    }

    private void ApplyAll()
    {
        if (_cross is not null && Day is { } day)
        {
            _cross.Seed = day.Seed;
            _cross.Inks = day.Inks;
        }

        ApplyStrokes(animate: false);
        ApplyText();
    }

    private void ApplyStrokes(bool animate)
    {
        if (_cross is null)
        {
            return;
        }

        // Seed first: a seed change redraws, which would cut the stroke animation short.
        _cross.Seed = Day?.Seed ?? 0;
        _cross.Inks = Day?.Inks;
        _cross.AnimateChanges = animate;
        _cross.Strokes = Day?.MarkStrokes ?? 0;
        _cross.AnimateChanges = true;
    }

    private void ApplyText()
    {
        if (_dayNumber is null || Day is not { } day)
        {
            return;
        }

        if (_cross is not null)
        {
            _cross.Seed = day.Seed;
            _cross.Inks = day.Inks;
        }

        AutomationProperties.SetName(this, day.AutomationName);
        AutomationProperties.SetAutomationId(this, $"CalendarDay{day.DayNumber}");
        _dayNumber.Text = day.DayNumber.ToString(CultureInfo.CurrentCulture);
        _date!.Text = day.DateLabel;
        _title!.Text = day.Title;
        _today!.Visibility = day.IsToday ? Visibility.Visible : Visibility.Collapsed;
        _bonus!.Visibility = day.BonusCount > 0 ? Visibility.Visible : Visibility.Collapsed;
        _date.Margin = new Thickness(0, 5, day.BonusCount > 0 ? 26 : 0, 0);
        _bonusText!.Text = day.BonusCount > 1 ? $"+{day.BonusCount}" : "+";

        // Locked and rest days read quieter; the X stays at full strength.
        _content!.Opacity = day.IsFuture ? 0.6 : 1;
        _title.Opacity = day.IsRestDay ? 0.7 : 1;
        UpdateTicks();
        UpdateCompact(ActualWidth);
    }

    private void UpdateCompact(double width)
    {
        if (_dayNumber is null || width <= 0)
        {
            return;
        }

        _isCompact = width < CompactWidth;
        _dayNumber.FontSize = Math.Clamp(width * 0.26, 14, 26);
        _date!.Visibility = _isCompact ? Visibility.Collapsed : Visibility.Visible;
        _title!.Visibility = _isCompact ? Visibility.Collapsed : Visibility.Visible;
        UpdateTicks();
    }

    private void UpdateTicks()
    {
        if (_ticks is null || _cross is null || Day is not { } day)
        {
            return;
        }

        // Full cells show ticks only for 3+ session days; compact cells have no title, so any multi-session day gets them.
        var show = day.ShowsTicks || (_isCompact && day.Sessions.Count > 1);
        _ticks.Children.Clear();
        _ticks.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (!show)
        {
            return;
        }

        var size = _isCompact ? 5 : 7;
        foreach (var session in day.Sessions)
        {
            _ticks.Children.Add(new Ellipse
            {
                Width = size,
                Height = size,
                Fill = session.IsDone ? _cross.MarkerBrush : null,
                Stroke = _date!.Foreground,
                StrokeThickness = session.IsDone ? 0 : 1,
            });
        }
    }

    private void Shake()
    {
        if (_shake is null)
        {
            return;
        }

        StopShake();
        DoubleAnimationUsingKeyFrames frames = new() { EnableDependentAnimation = true };
        double[] offsets = [0, -6, 6, -4, 4, -2, 0];
        for (var i = 0; i < offsets.Length; i++)
        {
            frames.KeyFrames.Add(new LinearDoubleKeyFrame { KeyTime = TimeSpan.FromMilliseconds(i * 50), Value = offsets[i] });
        }

        Storyboard.SetTarget(frames, _shake);
        Storyboard.SetTargetProperty(frames, nameof(TranslateTransform.X));
        _shakeStoryboard = new() { Children = { frames } };
        _shakeStoryboard.Completed += (_, _) =>
        {
            StopShake();
            if (Day is { } day)
            {
                day.ShakeRequested = false;
            }
        };
        _shakeStoryboard.Begin();
    }

    private void StopShake()
    {
        _shakeStoryboard?.Stop();
        _shakeStoryboard = null;
        if (_shake is not null)
        {
            _shake.X = 0;
        }
    }
}
