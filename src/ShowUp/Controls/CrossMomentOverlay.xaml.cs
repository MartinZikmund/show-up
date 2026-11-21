using System.Globalization;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Animation;
using ShowUp.Core.Marking;
using ShowUp.Services.Localization;
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

        var localizer = Localizer.Instance;
        DayNumberText.Text = info.DayNumber.ToString(CultureInfo.CurrentCulture);
        TitleText.Text = info.Title;
        CaptionText.Text = Format(localizer.GetString("CrossMomentCaptionFormat"), info.DayNumber, info.TotalDays, info.CurrentStreak);
        ProgressText.Text = info.SessionsDone < info.SessionsRequired
            ? Format(localizer.GetString("CrossMomentProgressFormat"), info.SessionsDone, info.SessionsRequired)
            : localizer.GetString("CrossMomentDayComplete");

        var animate = AnimationsEnabled;
        CaptionText.Opacity = 0;
        ProgressText.Opacity = 0;

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

    private static string Format(string format, params object[] args) => string.Format(CultureInfo.CurrentCulture, format, args);

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
            Fade(CaptionText, 0, 1, TimeSpan.FromMilliseconds(300));
            Fade(ProgressText, 0, 1, TimeSpan.FromMilliseconds(300));
        }
        else
        {
            CaptionText.Opacity = 1;
            ProgressText.Opacity = 1;
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
