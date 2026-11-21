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
/// <see cref="Inks"/> adds the user's drawn crosses on top, scaled to the control, so a mixed day shows both:
/// the control must fill the whole cell, or the ink lands shrunk and offset.
/// </summary>
[TemplatePart(Name = FirstPartName, Type = typeof(Path))]
[TemplatePart(Name = FirstUnderPartName, Type = typeof(Path))]
[TemplatePart(Name = SecondPartName, Type = typeof(Path))]
[TemplatePart(Name = SecondUnderPartName, Type = typeof(Path))]
[TemplatePart(Name = FirstTexturePartName, Type = typeof(Path))]
[TemplatePart(Name = SecondTexturePartName, Type = typeof(Path))]
[TemplatePart(Name = InkPartName, Type = typeof(InkView))]
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
    private const string FirstTexturePartName = "PART_FirstTexture";
    private const string SecondTexturePartName = "PART_SecondTexture";
    private const string InkPartName = "PART_Ink";
    private const int PartsPerStroke = 3;
    private const double BaseThickness = 9;
    private const double UnderStrokeWidthRatio = 1.35;

    // Ink texture: a thin, faint streak riding off-centre inside the main stroke.
    private const double TextureWidthRatio = 0.35;
    private const double TextureOffsetRatio = 0.2;
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

    public static readonly DependencyProperty InksProperty = DependencyProperty.Register(
        nameof(Inks), typeof(IReadOnlyList<string>), typeof(MarkerCrossControl),
        new PropertyMetadata(null, (d, _) => ((MarkerCrossControl)d).UpdateInk()));

    public static readonly DependencyProperty BleedBrushProperty = DependencyProperty.Register(
        nameof(BleedBrush), typeof(Brush), typeof(MarkerCrossControl), new PropertyMetadata(null));

    public static readonly DependencyProperty BleedOpacityProperty = DependencyProperty.Register(
        nameof(BleedOpacity), typeof(double), typeof(MarkerCrossControl), new PropertyMetadata(0.35));

    public static readonly DependencyProperty AnimateChangesProperty = DependencyProperty.Register(
        nameof(AnimateChanges), typeof(bool), typeof(MarkerCrossControl), new PropertyMetadata(true));

    private readonly List<Storyboard> _running = [];
    private readonly List<DispatcherQueueTimer> _pendingFeedback = [];
    // Per stroke: main, under, texture.
    private Path?[] _parts = new Path?[2 * PartsPerStroke];
    private InkView? _ink;
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

    /// <summary>
    /// Drawn crosses (<see cref="InkCodec"/> JSON), one per session crossed by drawing. <see cref="Strokes"/>
    /// stays the count of generated strokes, so set it from the auto-crossed sessions only.
    /// </summary>
    public IReadOnlyList<string>? Inks
    {
        get => (IReadOnlyList<string>?)GetValue(InksProperty);
        set => SetValue(InksProperty, value);
    }

    /// <summary>The wider stroke under the ink; <c>MarkerBleedBrush</c>, tinted toward the board on the dark theme.</summary>
    public Brush? BleedBrush
    {
        get => (Brush?)GetValue(BleedBrushProperty);
        set => SetValue(BleedBrushProperty, value);
    }

    /// <summary>The bleed's opacity; the style sets it from <c>MarkerBleedOpacity</c>.</summary>
    public double BleedOpacity
    {
        get => (double)GetValue(BleedOpacityProperty);
        set => SetValue(BleedOpacityProperty, value);
    }

    public bool AnimateChanges
    {
        get => (bool)GetValue(AnimateChangesProperty);
        set => SetValue(AnimateChangesProperty, value);
    }

    private static int ClampStrokes(int value) => Math.Clamp(value, 0, 2);

    private static readonly Lazy<UISettings?> _uiSettings = new(CreateUISettings);

    private static bool AnimationsEnabled => _uiSettings.Value?.AnimationsEnabled ?? true;

    private static UISettings? CreateUISettings()
    {
        try
        {
            return new UISettings();
        }
        catch
        {
            return null;
        }
    }

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _parts =
        [
            GetTemplateChild(FirstPartName) as Path,
            GetTemplateChild(FirstUnderPartName) as Path,
            GetTemplateChild(FirstTexturePartName) as Path,
            GetTemplateChild(SecondPartName) as Path,
            GetTemplateChild(SecondUnderPartName) as Path,
            GetTemplateChild(SecondTexturePartName) as Path,
        ];

        _ink = GetTemplateChild(InkPartName) as InkView;
        UpdateInk();

        // Resolve early: some heads load sounds asynchronously, and the first stroke shouldn't be silent.
        _feedback ??= App.Current.Services.GetService<IMarkingFeedback>();
        Redraw();
    }

    private void UpdateInk()
    {
        if (_ink is not null)
        {
            _ink.Strokes = Inks is { Count: > 0 } inks ? [.. inks.SelectMany(InkCodec.Decode)] : [];
        }
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
        if (!TryLayout(out var strokes))
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
        if (!TryLayout(out var strokes))
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
            last = AnimateStroke(i, strokes[i].Length, delay);
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

    private Storyboard AnimateStroke(int index, double length, TimeSpan delay)
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

    private bool TryLayout(out MarkerStroke[] strokes)
    {
        var (width, height) = (ActualWidth, ActualHeight);
        if (width <= 0 || height <= 0 || _parts[0] is null)
        {
            strokes = [];
            return false;
        }

        var (first, second) = MarkerGeometry.ForCell(Seed, width, height);
        strokes = [first, second];
        var thickness = BaseThickness * Math.Max(0.6, Math.Min(width, height) / ReferenceCellSize) * StrokeThicknessScale;

        for (var i = 0; i < 2; i++)
        {
            var strokeThickness = thickness * MarkerGeometry.WidthFactor(Seed, i);
            SetThickness(Part(i, 0), strokeThickness);
            SetThickness(Part(i, 1), strokeThickness * UnderStrokeWidthRatio);
            SetThickness(Part(i, 2), strokeThickness * TextureWidthRatio);
        }

        return true;
    }

    private static void SetThickness(Path? path, double thickness)
    {
        if (path is not null)
        {
            path.StrokeThickness = thickness;
        }
    }

    private Path? Part(int stroke, int kind) => _parts[stroke * PartsPerStroke + kind];

    private void SetStroke(int index, MarkerStroke stroke, bool visible)
    {
        foreach (var path in StrokeParts(index))
        {
            var shape = path == Part(index, 2) ? stroke.Offset((Part(index, 0)?.StrokeThickness ?? 0) * TextureOffsetRatio) : stroke;
            path.Data = ToGeometry(shape);
            path.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private IEnumerable<Path> StrokeParts(int index)
    {
        for (var kind = 0; kind < PartsPerStroke; kind++)
        {
            if (Part(index, kind) is { } path)
            {
                yield return path;
            }
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
