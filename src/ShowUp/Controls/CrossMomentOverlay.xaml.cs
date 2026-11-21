using System.Globalization;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Animation;
using ShowUp.Core.Marking;
using VirtualKey = Windows.System.VirtualKey;
using Windows.UI.ViewManagement;

namespace ShowUp.Controls;

/// <summary>The "cross moment": the day's X drawn big, then a caption, then it gets out of the way.</summary>
public sealed partial class CrossMomentOverlay : UserControl, ICrossMoment
{
    private static readonly TimeSpan AutoDismissDelay = TimeSpan.FromSeconds(2.5);
    private static readonly TimeSpan CaptionDelay = TimeSpan.FromMilliseconds(60);
    private const double MaxCellSize = 360;
    private const double CellToShorterSideRatio = 0.7;
    private const double CardWidth = 480;
    private const double CardPadding = 32;

    private readonly DispatcherQueueTimer _dismissTimer;
    private readonly DispatcherQueueTimer _captionTimer;
    private TaskCompletionSource? _shown;
    private CrossMomentInfo? _info;
    private bool _awaitingStrokes;
    private UIElement? _previousFocus;

    public CrossMomentOverlay()
    {
        InitializeComponent();

        _dismissTimer = DispatcherQueue.CreateTimer();
        _dismissTimer.IsRepeating = false;
        _dismissTimer.Interval = AutoDismissDelay;
        _dismissTimer.Tick += (_, _) => Dismiss();

        _captionTimer = DispatcherQueue.CreateTimer();
        _captionTimer.IsRepeating = false;
        _captionTimer.Interval = CaptionDelay;
        _captionTimer.Tick += (_, _) => RevealCaption();

        Cross.StrokesDrawn += (_, _) => OnStrokesDrawn();
    }

    public bool IsOpen => _shown is not null;

    public Task ShowAsync(CrossMomentInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);
        Dismiss();

        _info = info;
        _shown = new(TaskCreationOptions.RunContinuationsAsynchronously);

        var caption = CrossMomentCaption.Create(info, App.Current.Services.GetRequiredService<IStringLocalizer>());
        DayNumberText.Text = info.DayNumber.ToString(CultureInfo.CurrentCulture);
        TitleText.Text = info.Title;
        DayText.Text = caption.Day;
        StreakText.Text = caption.Streak ?? string.Empty;
        StreakPanel.Visibility = caption.Streak is null ? Visibility.Collapsed : Visibility.Visible;
        ProgressText.Text = caption.Progress;
        AutomationProperties.SetName(CaptionPanel, caption.Streak is null ? caption.Day : $"{caption.Day}, {caption.Streak}");

        var animate = AnimationsEnabled;
        CaptionPanel.Opacity = 0;

        Cross.AnimateChanges = false;
        Cross.Seed = info.Seed;
        Cross.Strokes = info.StrokesBefore;

        _previousFocus = XamlRoot is { } root ? FocusManager.GetFocusedElement(root) as UIElement : null;
        Visibility = Visibility.Visible;
        UpdateCellSize();

        // Measure now so the strokes are computed for the cell's final size.
        UpdateLayout();
        Focus(FocusState.Programmatic);

        if (animate)
        {
            Fade(this, 0, 1, TimeSpan.FromMilliseconds(180));
        }
        else
        {
            Opacity = 1;
        }

        _awaitingStrokes = true;
        if (info.StrokesAfter > info.StrokesBefore)
        {
            Cross.AnimateChanges = true;
            Cross.Strokes = info.StrokesAfter;
        }
        else
        {
            OnStrokesDrawn();
        }

        return _shown.Task;
    }

    public void Dismiss()
    {
        _dismissTimer.Stop();
        _captionTimer.Stop();
        _awaitingStrokes = false;
        if (_shown is not { } shown)
        {
            return;
        }

        _shown = null;
        Visibility = Visibility.Collapsed;

        // The cell keeps the final X only while open; the next show starts from its own StrokesBefore.
        Cross.AnimateChanges = false;
        _previousFocus?.Focus(FocusState.Programmatic);
        _previousFocus = null;
        shown.TrySetResult();
    }

    private static bool AnimationsEnabled
    {
        get
        {
            try
            {
                return new UISettings().AnimationsEnabled;
            }
            catch
            {
                return true;
            }
        }
    }

    private void OnStrokesDrawn()
    {
        if (!_awaitingStrokes)
        {
            return;
        }

        _awaitingStrokes = false;
        if (AnimationsEnabled)
        {
            _captionTimer.Start();
        }
        else
        {
            RevealCaption();
        }

        _dismissTimer.Start();
    }

    private void RevealCaption()
    {
        if (_info is { StrokesAfter: >= 2 } info && info.StrokesAfter > info.StrokesBefore)
        {
            App.Current.Services.GetService<IMarkingFeedback>()?.CrossLanded();
        }

        if (AnimationsEnabled)
        {
            Fade(CaptionPanel, 0, 1, TimeSpan.FromMilliseconds(300));
        }
        else
        {
            CaptionPanel.Opacity = 1;
        }
    }

    private static void Fade(UIElement target, double from, double to, TimeSpan duration)
    {
        target.Opacity = to;
        DoubleAnimation animation = new()
        {
            From = from,
            To = to,
            Duration = duration,
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        Storyboard.SetTarget(animation, target);
        Storyboard.SetTargetProperty(animation, nameof(Opacity));
        Storyboard storyboard = new();
        storyboard.Children.Add(animation);
        storyboard.Begin();
    }

    private void Root_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateCellSize();

    private void UpdateCellSize()
    {
        var (width, height) = (Root.ActualWidth, Root.ActualHeight);
        if (width <= 0 || height <= 0)
        {
            return;
        }

        // ~70% of the shorter side, but never wider than the card's content area.
        var available = width >= 640 ? CardWidth - 2 * CardPadding : width - 2 * CardPadding;
        var size = Math.Min(Math.Min(width, height) * CellToShorterSideRatio, Math.Min(available, MaxCellSize));
        BigCell.Width = size;
        BigCell.Height = size;

        // Keep the ink clear of the day number (top) and the title (bottom).
        Cross.Margin = new(size * 0.12, size * 0.2, size * 0.12, size * 0.12);
    }

    private void Root_Tapped(object sender, TappedRoutedEventArgs e)
    {
        e.Handled = true;
        Dismiss();
    }

    private void Overlay_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key is VirtualKey.Escape or VirtualKey.Enter or VirtualKey.Space)
        {
            e.Handled = true;
            Dismiss();
        }
    }
}
