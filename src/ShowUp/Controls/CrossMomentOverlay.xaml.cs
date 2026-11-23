using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Animation;
using ShowUp.Core.Marking;
using ShowUp.Services.Localization;
using VirtualKey = Windows.System.VirtualKey;
using Windows.UI.ViewManagement;

namespace ShowUp.Controls;

/// <summary>
/// The "cross moment": the day's X drawn big, then a caption, then it gets out of the way.
/// In draw mode (<see cref="DrawAsync"/>) the user draws the cross on the big square instead.
/// </summary>
public sealed partial class CrossMomentOverlay : UserControl, ICrossMoment
{
    private static readonly TimeSpan AutoDismissDelay = TimeSpan.FromSeconds(2.5);
    private static readonly TimeSpan DrawnDismissDelay = TimeSpan.FromSeconds(2.2);

    // Long enough to reach "Log your results"; the moment still goes away on its own.
    private static readonly TimeSpan ResultsDismissDelay = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan CaptionDelay = TimeSpan.FromMilliseconds(60);

    // The tap or click that finished the drawing must not also dismiss it.
    private static readonly TimeSpan FinishTapGuard = TimeSpan.FromMilliseconds(400);
    private const double MaxCellSize = 360;
    private const double CellToShorterSideRatio = 0.7;
    private const double CardWidth = 480;
    private const double CardPadding = 32;

    private readonly DispatcherQueueTimer _dismissTimer;
    private readonly DispatcherQueueTimer _captionTimer;
    private TaskCompletionSource? _shown;
    private TaskCompletionSource<CrossMomentResult>? _drawn;
    private CrossMomentDrawRequest? _drawRequest;
    private CrossMomentInfo? _info;
    private CrossMomentCaption? _caption;
    private readonly List<Storyboard> _fades = [];
    private bool _awaitingStrokes;
    private UIElement? _previousFocus;
    private InkSession? _session;
    private DrawState _drawState;
    private long _finishedAt;
    private bool _inkCrossed;
    private bool _logResultsHovered;

    public CrossMomentOverlay()
    {
        InitializeComponent();

        _dismissTimer = DispatcherQueue.CreateTimer();
        _dismissTimer.IsRepeating = false;
        _dismissTimer.Interval = AutoDismissDelay;
        // A finished program waits for an answer; everything else gets out of the way on its own.
        _dismissTimer.Tick += (_, _) =>
        {
            if (FinishPanel.Visibility != Visibility.Visible)
            {
                Dismiss();
            }
        };

        _captionTimer = DispatcherQueue.CreateTimer();
        _captionTimer.IsRepeating = false;
        _captionTimer.Interval = CaptionDelay;
        _captionTimer.Tick += (_, _) => RevealCaption();

        Cross.StrokesDrawn += (_, _) => OnStrokesDrawn();
    }

    private enum DrawState
    {
        None,
        Drawing,
        Finished,
    }

    public bool IsOpen => _shown is not null;

    /// <summary>The page content under the overlay; hidden from screen readers while the overlay is open.</summary>
    public UIElement? ShellContent { get; set; }

    public Task ShowAsync(CrossMomentInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);
        Dismiss();

        var shown = Open(info);
        PlayStrokes(info);
        return shown;
    }

    public Task<CrossMomentResult> DrawAsync(CrossMomentDrawRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        Dismiss();

        var drawn = _drawn = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _drawRequest = request;
        _session = new(App.Current.Services.GetService<TimeProvider>() ?? TimeProvider.System);

        // Finished can fire on the timer's thread, where DispatcherQueue isn't reachable: capture it here.
        var dispatcher = DispatcherQueue;
        _session.Finished += (_, _) => dispatcher.TryEnqueue(FinishDrawn);
        _drawState = DrawState.Drawing;

        Pad.Session = _session;
        Pad.PriorStrokes = [.. request.PriorInks.SelectMany(InkCodec.Decode)];
        Pad.IsHitTestVisible = true;
        Pad.Visibility = Visibility.Visible;
        DrawPanel.Visibility = Visibility.Visible;
        UpdateDrawControls();

        var openedByKeyboard = (XamlRoot is { } root ? FocusManager.GetFocusedElement(root) : null) is Control { FocusState: FocusState.Keyboard };
        _ = Open(request.Info);
        DoItForMeButton.Focus(openedByKeyboard ? FocusState.Keyboard : FocusState.Programmatic);
        Announce(DrawPanel, Localizer.Instance["DrawAnnouncement"]);
        return drawn.Task;
    }

    public void Dismiss()
    {
        _dismissTimer.Stop();
        _captionTimer.Stop();
        _awaitingStrokes = false;
        EndDrawing();
        if (_shown is not { } shown)
        {
            return;
        }

        _shown = null;
        Visibility = Visibility.Collapsed;
        SetShellAccessible(true);
        StopFades();
        Confetti.Stop();
        FinishPanel.Visibility = Visibility.Collapsed;
        HideCaptionFromAutomation();

        // The cell keeps the final X only while open; the next show starts from its own StrokesBefore.
        Cross.AnimateChanges = false;
        _previousFocus?.Focus(FocusState.Programmatic);
        _previousFocus = null;
        shown.TrySetResult();
    }

    private static bool AnimationsEnabled => App.Current.Services.GetService<IMotionPreference>()?.AnimationsEnabled ?? true;

    private static readonly Lazy<AccessibilitySettings?> _accessibility = new(() =>
    {
        try
        {
            return new AccessibilitySettings();
        }
        catch
        {
            return null;
        }
    });

    private static bool IsHighContrast => _accessibility.Value?.HighContrast ?? false;

    private void SetShellAccessible(bool accessible)
    {
        if (ShellContent is { } shell)
        {
            AutomationProperties.SetAccessibilityView(shell, accessible ? AccessibilityView.Content : AccessibilityView.Raw);
        }
    }

    private Task Open(CrossMomentInfo info)
    {
        _info = info;
        _inkCrossed = false;
        _shown = new(TaskCreationOptions.RunContinuationsAsynchronously);

        var caption = _caption = CrossMomentCaption.Create(info, App.Current.Services.GetRequiredService<IStringLocalizer>());
        DayNumberText.Text = info.DayNumber.ToString(CultureInfo.CurrentCulture);
        TitleText.Text = info.Title;
        DayText.Text = caption.Day;
        StreakText.Text = caption.Streak ?? string.Empty;
        StreakPanel.Visibility = caption.Streak is null ? Visibility.Collapsed : Visibility.Visible;
        ProgressText.Text = caption.Progress;
        MilestoneText.Text = caption.Milestone ?? string.Empty;
        MilestoneText.Visibility = caption.Milestone is null ? Visibility.Collapsed : Visibility.Visible;
        FinishPanel.Visibility = Visibility.Collapsed;
        Confetti.Stop();
        HideCaptionFromAutomation();
        CaptionPanel.Opacity = 0;
        BelowCell.MinHeight = 0;
        LogResultsButton.Visibility = Visibility.Collapsed;
        _logResultsHovered = false;

        Cross.AnimateChanges = false;
        Cross.Seed = info.Seed;
        Cross.Strokes = info.StrokesBefore;

        _previousFocus = XamlRoot is { } root ? FocusManager.GetFocusedElement(root) as UIElement : null;
        Visibility = Visibility.Visible;
        SetShellAccessible(false);
        UpdateCellSize();

        // Measure now so the strokes are computed for the cell's final size.
        UpdateLayout();
        Focus(FocusState.Programmatic);

        if (AnimationsEnabled)
        {
            Fade(this, 0, 1, TimeSpan.FromMilliseconds(180));
        }
        else
        {
            Opacity = 1;
        }

        return _shown.Task;
    }

    private void PlayStrokes(CrossMomentInfo info)
    {
        _dismissTimer.Interval = info.LogResults is null ? AutoDismissDelay : ResultsDismissDelay;
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
        if (!_inkCrossed && _info is { StrokesAfter: >= 2 } info && info.StrokesAfter > info.StrokesBefore)
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

        AnnounceCaption();
        if (_info?.LogResults is not null)
        {
            LogResultsButton.Visibility = Visibility.Visible;
        }

        if (_info is { Milestone: not null } celebrated)
        {
            App.Current.Services.GetService<IMarkingFeedback>()?.SessionTicked();
            if (AnimationsEnabled && !IsHighContrast)
            {
                Confetti.Play(celebrated.Seed);
            }
        }

        if (_info?.Finish is not null)
        {
            _dismissTimer.Stop();
            FinishPanel.Visibility = Visibility.Visible;
            FinishProgramButton.Focus(FocusState.Programmatic);
        }
    }

    private async void FinishProgramButton_Click(object sender, RoutedEventArgs e)
    {
        var finish = _info?.Finish;
        Dismiss();
        if (finish is null)
        {
            return;
        }

        try
        {
            await finish();
        }
        catch (Exception ex)
        {
            App.Current.Services.GetService<ILogger<CrossMomentOverlay>>()?.LogError(ex, "Finishing the program failed.");
        }
    }

    // The program stays active; the Calendar keeps offering to finish it.
    private void KeepOpenButton_Click(object sender, RoutedEventArgs e) => Dismiss();

    private async void LogResultsButton_Click(object sender, RoutedEventArgs e)
    {
        var logResults = _info?.LogResults;
        Dismiss();
        if (logResults is null)
        {
            return;
        }

        try
        {
            await logResults();
        }
        catch (Exception ex)
        {
            App.Current.Services.GetService<ILogger<CrossMomentOverlay>>()?.LogError(ex, "Opening Log your results failed.");
        }
    }

    // Reaching for the button must not race the auto-dismiss; leaving it starts the countdown again.
    private void LogResultsButton_Engaged(object sender, RoutedEventArgs e) => _dismissTimer.Stop();

    private void LogResultsButton_Released(object sender, RoutedEventArgs e)
    {
        if (IsOpen && !_logResultsHovered && LogResultsButton.FocusState == FocusState.Unfocused)
        {
            _dismissTimer.Start();
        }
    }

    private void LogResultsButton_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        _logResultsHovered = true;
        _dismissTimer.Stop();
    }

    private void LogResultsButton_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        _logResultsHovered = false;
        LogResultsButton_Released(sender, e);
    }

    private void Pad_InkChanged(InkPad sender, object? args) => UpdateDrawControls();

    private void UpdateDrawControls()
    {
        if (_session is not { } session)
        {
            return;
        }

        InkMeter.Value = Math.Round(session.Score * 100);
        // Like the prototype, Done shows on lift, never mid-stroke.
        DoneButton.Visibility = session.Counts && !session.IsDrawing ? Visibility.Visible : Visibility.Collapsed;
        UndoButton.IsEnabled = session.Strokes.Count > 0;
    }

    private void DoneButton_Click(object sender, RoutedEventArgs e) => _session?.Finish();

    private void UndoButton_Click(object sender, RoutedEventArgs e)
    {
        _session?.Undo();
        Pad.Refresh();
        UpdateDrawControls();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => Dismiss();

    private void DoItForMeButton_Click(object sender, RoutedEventArgs e) => DoItForMe();

    // The settled ink (or Done): persist it, a stronger tick + squeak, then the caption.
    private void FinishDrawn()
    {
        if (_drawState != DrawState.Drawing || _session is not { IsFinished: true } session)
        {
            return;
        }

        _drawState = DrawState.Finished;
        _finishedAt = Environment.TickCount64;
        _inkCrossed = true;
        HideDrawControls();
        App.Current.Services.GetService<IMarkingFeedback>()?.DayTaken();
        _drawn?.TrySetResult(CrossMomentResult.Drawn(session.Encode()));

        // A drawn session that completes a day with auto sessions lands the rest of the X.
        if (_drawRequest is { DrawnStrokesAfter: var after } && after > Cross.Strokes)
        {
            Cross.AnimateChanges = AnimationsEnabled;
            Cross.Strokes = after;
        }

        RevealCaption();
        _dismissTimer.Interval = _info?.LogResults is null ? DrawnDismissDelay : ResultsDismissDelay;
        _dismissTimer.Start();
    }

    private void DoItForMe()
    {
        if (_drawState != DrawState.Drawing || _info is not { } info)
        {
            return;
        }

        _drawState = DrawState.Finished;
        _finishedAt = Environment.TickCount64;
        _session?.Dispose();
        Pad.Session = null;
        HideDrawControls();
        _drawn?.TrySetResult(CrossMomentResult.Auto);
        Focus(FocusState.Programmatic);
        PlayStrokes(info);
    }

    private void HideDrawControls()
    {
        BelowCell.MinHeight = BelowCell.ActualHeight;
        Pad.IsHitTestVisible = false;
        DrawPanel.Visibility = Visibility.Collapsed;
        DoneButton.Visibility = Visibility.Collapsed;

        // Focus would otherwise be left on a collapsed button.
        Focus(FocusState.Programmatic);
    }

    private void EndDrawing()
    {
        if (_drawState == DrawState.None)
        {
            return;
        }

        _drawState = DrawState.None;
        _session?.Dispose();
        _session = null;
        Pad.Session = null;
        Pad.PriorStrokes = [];
        Pad.Visibility = Visibility.Collapsed;
        DrawPanel.Visibility = Visibility.Collapsed;
        _drawn?.TrySetResult(CrossMomentResult.Cancelled);
        _drawn = null;
        _drawRequest = null;
    }

    private bool WithinFinishGuard => Environment.TickCount64 - _finishedAt < FinishTapGuard.TotalMilliseconds;

    // LiveSetting alone announces nothing: the live region has to raise LiveRegionChanged itself.
    private void AnnounceCaption()
    {
        if (_caption is not { } caption)
        {
            return;
        }

        AutomationProperties.SetAccessibilityView(CaptionPanel, AccessibilityView.Content);
        Announce(CaptionPanel, caption.Announcement);
    }

    private static void Announce(FrameworkElement liveRegion, string text)
    {
        AutomationProperties.SetName(liveRegion, text);
        var peer = FrameworkElementAutomationPeer.FromElement(liveRegion) ?? FrameworkElementAutomationPeer.CreatePeerForElement(liveRegion);
        peer?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }

    // While invisible the caption mustn't be found (or read) by screen readers.
    private void HideCaptionFromAutomation()
    {
        AutomationProperties.SetName(CaptionPanel, string.Empty);
        AutomationProperties.SetAccessibilityView(CaptionPanel, AccessibilityView.Raw);
    }

    private void Fade(UIElement target, double from, double to, TimeSpan duration)
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
        _fades.Add(storyboard);
        storyboard.Begin();
    }

    // A finished fade holds its end value over the local one, so the next show's caption would start visible.
    private void StopFades()
    {
        foreach (var fade in _fades)
        {
            fade.Stop();
        }

        _fades.Clear();
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

    // The backdrop (wide layout): cancels while drawing, dismisses once done.
    private void Root_Tapped(object sender, TappedRoutedEventArgs e)
    {
        e.Handled = true;
        if (_drawState == DrawState.Finished && WithinFinishGuard)
        {
            return;
        }

        Dismiss();
    }

    // While drawing, taps on the card (buttons, margins) never dismiss; once done they do, after the guard.
    private void Card_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (_drawState == DrawState.None)
        {
            return;
        }

        e.Handled = true;
        if (_drawState == DrawState.Finished && !WithinFinishGuard)
        {
            Dismiss();
        }
    }

    private void Overlay_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (_drawState == DrawState.Drawing)
        {
            if (e.Key == VirtualKey.Escape)
            {
                e.Handled = true;
                Dismiss();
            }
            else if (e.Key == VirtualKey.Enter && XamlRoot is { } root && FocusManager.GetFocusedElement(root) == DoItForMeButton)
            {
                e.Handled = true;
                DoItForMe();
            }

            return;
        }

        if (e.Key is VirtualKey.Escape or VirtualKey.Enter or VirtualKey.Space)
        {
            e.Handled = true;
            Dismiss();
        }
    }
}
