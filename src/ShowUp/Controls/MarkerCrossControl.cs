using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Shapes;
using Path = Microsoft.UI.Xaml.Shapes.Path;
using ShowUp.Core.Marking;
using Windows.UI.ViewManagement;

namespace ShowUp.Controls;

/// <summary>
/// Hand-drawn marker X: <see cref="Strokes"/> 1 draws the top-left to bottom-right diagonal, 2 completes the X.
/// Increases animate stroke by stroke (with sound + haptics), decreases redraw instantly.
/// </summary>
[TemplatePart(Name = FirstPartName, Type = typeof(Path))]
[TemplatePart(Name = FirstUnderPartName, Type = typeof(Path))]
[TemplatePart(Name = SecondPartName, Type = typeof(Path))]
[TemplatePart(Name = SecondUnderPartName, Type = typeof(Path))]
public sealed partial class MarkerCrossControl : Control
{
    private static readonly TimeSpan BaseStrokeDuration = TimeSpan.FromMilliseconds(180);
    private static readonly TimeSpan BaseStrokeGap = TimeSpan.FromMilliseconds(90);

#if DEBUG
    /// <summary>Slow motion for eyeballing and capturing frames (debug tools on the Today page).</summary>
    public static double DebugTimeScale { get; set; } = 1;

    private static TimeSpan StrokeDuration => BaseStrokeDuration * DebugTimeScale;

    private static TimeSpan StrokeGap => BaseStrokeGap * DebugTimeScale;
#else
    private static TimeSpan StrokeDuration => BaseStrokeDuration;

    private static TimeSpan StrokeGap => BaseStrokeGap;
#endif

    private const string FirstPartName = "PART_First";
    private const string FirstUnderPartName = "PART_FirstUnder";
    private const string SecondPartName = "PART_Second";
    private const string SecondUnderPartName = "PART_SecondUnder";
    private const double BaseThickness = 9;
    private const double UnderStrokeWidthRatio = 1.35;
    private const double ReferenceCellSize = 96;

    public static readonly DependencyProperty StrokesProperty = DependencyProperty.Register(
        nameof(Strokes), typeof(int), typeof(MarkerCrossControl),
        new PropertyMetadata(0, (d, e) => ((MarkerCrossControl)d).OnStrokesChanged((int)e.OldValue, (int)e.NewValue)));

    public static readonly DependencyProperty SeedProperty = DependencyProperty.Register(
        nameof(Seed), typeof(int), typeof(MarkerCrossControl),
        new PropertyMetadata(0, (d, _) => ((MarkerCrossControl)d).Redraw()));

    public static readonly DependencyProperty MarkerBrushProperty = DependencyProperty.Register(
        nameof(MarkerBrush), typeof(Brush), typeof(MarkerCrossControl), new PropertyMetadata(null));

    public static readonly DependencyProperty StrokeThicknessScaleProperty = DependencyProperty.Register(
        nameof(StrokeThicknessScale), typeof(double), typeof(MarkerCrossControl),
        new PropertyMetadata(1.0, (d, _) => ((MarkerCrossControl)d).Redraw()));

    public static readonly DependencyProperty AnimateChangesProperty = DependencyProperty.Register(
        nameof(AnimateChanges), typeof(bool), typeof(MarkerCrossControl), new PropertyMetadata(true));

    private readonly List<Storyboard> _running = [];
    private readonly List<DispatcherQueueTimer> _pendingFeedback = [];
    private Path?[] _parts = new Path?[4];
    private int? _pendingAnimationFrom;
    private IMarkingFeedback? _feedback;

    public MarkerCrossControl()
    {
        SizeChanged += (_, _) => OnSizeChanged();
        Unloaded += (_, _) => StopAnimations();
    }

    /// <summary>Raised when the strokes from the last increase have finished drawing (immediately when not animated).</summary>
    public event TypedEventHandler<MarkerCrossControl, object?>? StrokesDrawn;

    public int Strokes
    {
        get => (int)GetValue(StrokesProperty);
        set => SetValue(StrokesProperty, value);
    }

    public int Seed
    {
        get => (int)GetValue(SeedProperty);
        set => SetValue(SeedProperty, value);
    }

    public Brush? MarkerBrush
    {
        get => (Brush?)GetValue(MarkerBrushProperty);
        set => SetValue(MarkerBrushProperty, value);
    }

    public double StrokeThicknessScale
    {
        get => (double)GetValue(StrokeThicknessScaleProperty);
        set => SetValue(StrokeThicknessScaleProperty, value);
    }

    public bool AnimateChanges
    {
        get => (bool)GetValue(AnimateChangesProperty);
        set => SetValue(AnimateChangesProperty, value);
    }

    private static int ClampStrokes(int value) => Math.Clamp(value, 0, 2);

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

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _parts =
        [
            GetTemplateChild(FirstPartName) as Path,
            GetTemplateChild(FirstUnderPartName) as Path,
            GetTemplateChild(SecondPartName) as Path,
            GetTemplateChild(SecondUnderPartName) as Path,
        ];
        Redraw();
    }

    private void OnStrokesChanged(int oldValue, int newValue)
    {
        oldValue = ClampStrokes(oldValue);
        newValue = ClampStrokes(newValue);

        if (newValue > oldValue && AnimateChanges && AnimationsEnabled)
        {
            if (ActualWidth > 0 && ActualHeight > 0 && _parts[0] is not null)
            {
                Animate(oldValue, newValue);
            }
            else
            {
                // Not laid out yet: draw on the first size we get.
                _pendingAnimationFrom = oldValue;
            }

            return;
        }

        _pendingAnimationFrom = null;
        Redraw();
        StrokesDrawn?.Invoke(this, null);
    }

    private void OnSizeChanged()
    {
        if (_pendingAnimationFrom is { } from && ActualWidth > 0 && ActualHeight > 0)
        {
            _pendingAnimationFrom = null;
            var to = ClampStrokes(Strokes);
            if (to > from)
            {
                Animate(from, to);
            }
            else
            {
                Redraw();
                StrokesDrawn?.Invoke(this, null);
            }

            return;
        }

        // Geometry is size-dependent; a resize mid-animation just lands the X.
        var wasAnimating = _running.Count > 0;
        Redraw();
        if (wasAnimating)
        {
            StrokesDrawn?.Invoke(this, null);
        }
    }

    private void Redraw()
    {
        StopAnimations();
        if (!TryLayout(out var strokes, out _))
        {
            return;
        }

        var visible = ClampStrokes(Strokes);
        for (var i = 0; i < 2; i++)
        {
            SetStroke(i, strokes[i], visible > i);
        }
    }

    private void Animate(int from, int to)
    {
        StopAnimations();
        if (!TryLayout(out var strokes, out var thickness))
        {
            return;
        }

        for (var i = 0; i < 2; i++)
        {
            SetStroke(i, strokes[i], visible: i < from);
        }

        _feedback ??= App.Current.Services.GetService<IMarkingFeedback>();
        var delay = TimeSpan.Zero;
        Storyboard? last = null;
        for (var i = from; i < to; i++)
        {
            SetStroke(i, strokes[i], visible: true);
            last = AnimateStroke(i, strokes[i].Length, thickness, delay);
            ScheduleFeedback(delay);
            delay += StrokeDuration + StrokeGap;
        }

        if (last is not null)
        {
            last.Completed += (_, _) =>
            {
                // Stop releases the HoldEnd values, so a reused control starts its next draw from scratch.
                StopAnimations();
                StrokesDrawn?.Invoke(this, null);
            };
        }
    }

    private Storyboard AnimateStroke(int index, double length, double thickness, TimeSpan delay)
    {
        Storyboard storyboard = new();
        foreach (var path in StrokeParts(index))
        {
            // Dash lengths are in multiples of StrokeThickness. Hidden = the dash (and its round cap) sits
            // just before the start; the gap is longer than the path so nothing else peeks through.
            var unit = length / path.StrokeThickness;
            var hidden = unit + 1;
            path.StrokeDashArray = [unit, unit + 2];
            path.StrokeDashOffset = hidden;

            DoubleAnimation animation = new()
            {
                From = hidden,
                To = 0,
                // On the child, not the storyboard: Uno ignores Storyboard.BeginTime.
                BeginTime = delay,
                Duration = StrokeDuration,
                EnableDependentAnimation = true,
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            };
            Storyboard.SetTarget(animation, path);
            Storyboard.SetTargetProperty(animation, nameof(Shape.StrokeDashOffset));
            storyboard.Children.Add(animation);
        }

        _running.Add(storyboard);
        storyboard.Begin();
        return storyboard;
    }

    private void ScheduleFeedback(TimeSpan delay)
    {
        if (_feedback is not { } feedback)
        {
            return;
        }

        if (delay == TimeSpan.Zero)
        {
            feedback.StrokeDrawn();
            return;
        }

        var timer = DispatcherQueue.CreateTimer();
        timer.Interval = delay;
        timer.IsRepeating = false;
        timer.Tick += (t, _) =>
        {
            t.Stop();
            _pendingFeedback.Remove(t);
            feedback.StrokeDrawn();
        };
        _pendingFeedback.Add(timer);
        timer.Start();
    }

    private void StopAnimations()
    {
        foreach (var storyboard in _running)
        {
            storyboard.Stop();
        }

        _running.Clear();
        foreach (var timer in _pendingFeedback)
        {
            timer.Stop();
        }

        _pendingFeedback.Clear();
        ClearDashes();
    }

    private void ClearDashes()
    {
        foreach (var path in _parts)
        {
            if (path is not null)
            {
                path.StrokeDashArray = [];
                path.StrokeDashOffset = 0;
            }
        }
    }

    private bool TryLayout(out MarkerStroke[] strokes, out double thickness)
    {
        var (width, height) = (ActualWidth, ActualHeight);
        if (width <= 0 || height <= 0 || _parts[0] is null)
        {
            strokes = [];
            thickness = 0;
            return false;
        }

        var (first, second) = MarkerGeometry.Create(Seed, width, height);
        strokes = [first, second];
        thickness = BaseThickness * Math.Max(0.6, Math.Min(width, height) / ReferenceCellSize) * StrokeThicknessScale;

        for (var i = 0; i < 2; i++)
        {
            if (_parts[i * 2] is { } main)
            {
                main.StrokeThickness = thickness;
            }

            if (_parts[i * 2 + 1] is { } under)
            {
                under.StrokeThickness = thickness * UnderStrokeWidthRatio;
            }
        }

        return true;
    }

    private void SetStroke(int index, MarkerStroke stroke, bool visible)
    {
        foreach (var path in StrokeParts(index))
        {
            path.Data = ToGeometry(stroke);
            path.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private IEnumerable<Path> StrokeParts(int index)
    {
        if (_parts[index * 2] is { } main)
        {
            yield return main;
        }

        if (_parts[index * 2 + 1] is { } under)
        {
            yield return under;
        }
    }

    private static PathGeometry ToGeometry(MarkerStroke stroke)
    {
        PathFigure figure = new() { StartPoint = stroke.Start, IsClosed = false, IsFilled = false };
        figure.Segments.Add(new QuadraticBezierSegment { Point1 = stroke.Control, Point2 = stroke.End });
        PathGeometry geometry = new();
        geometry.Figures.Add(figure);
        return geometry;
    }
}
