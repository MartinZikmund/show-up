using Microsoft.UI.Dispatching;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml.Input;
using ShowUp.Core.Marking;

namespace ShowUp.Controls;

/// <summary>
/// The draw-it-yourself surface: captures one pointer (finger, pen or left mouse button), feeds an
/// <see cref="InkSession"/>, renders it live over the day's earlier ink (50%) and drives the scratch sound.
/// </summary>
public sealed partial class InkPad : Grid
{
    // The scratch fades out when the pen holds still (moves under the min distance never report speed).
    private static readonly TimeSpan StillDelay = TimeSpan.FromMilliseconds(60);
    private const double MinFrameMilliseconds = 8;
    private const double FrameMilliseconds = 16;

    public static readonly DependencyProperty MarkerBrushProperty = DependencyProperty.Register(
        nameof(MarkerBrush), typeof(Brush), typeof(InkPad),
        new PropertyMetadata(null, (d, e) => ((InkPad)d).ForEachView(v => v.MarkerBrush = (Brush?)e.NewValue)));

    public static readonly DependencyProperty BleedOpacityProperty = DependencyProperty.Register(
        nameof(BleedOpacity), typeof(double), typeof(InkPad),
        new PropertyMetadata(0.35, (d, e) => ((InkPad)d).ForEachView(v => v.BleedOpacity = (double)e.NewValue)));

    private readonly InkView _prior = new() { InkOpacity = 0.5 };
    private readonly InkView _ink = new();
    private readonly DispatcherQueueTimer _stillTimer;
    private InkSession? _session;
    private uint? _pointerId;
    private InkPoint _lastPoint;
    private long _lastTimestamp;
    private IMarkingFeedback? _feedback;

    public InkPad()
    {
        // Hit-testable everywhere, not just on ink.
        Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        Children.Add(_prior);
        Children.Add(_ink);

        _stillTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _stillTimer.IsRepeating = false;
        _stillTimer.Interval = StillDelay;
        _stillTimer.Tick += (_, _) => _feedback?.ScratchSpeed(0);

        PointerPressed += OnPointerPressed;
        PointerMoved += OnPointerMoved;
        PointerReleased += OnPointerReleased;
        PointerCanceled += OnPointerCanceled;
        PointerCaptureLost += OnPointerCaptureLost;
        Tapped += OnTapped;
        Unloaded += (_, _) => EndStroke();
    }

    /// <summary>Raised after every change to the ink (stroke start, point, stroke end, undo).</summary>
    public event TypedEventHandler<InkPad, object?>? InkChanged;

    public Brush? MarkerBrush
    {
        get => (Brush?)GetValue(MarkerBrushProperty);
        set => SetValue(MarkerBrushProperty, value);
    }

    public double BleedOpacity
    {
        get => (double)GetValue(BleedOpacityProperty);
        set => SetValue(BleedOpacityProperty, value);
    }

    public InkSession? Session
    {
        get => _session;
        set
        {
            EndStroke();
            _session = value;
            Refresh();
        }
    }

    public IReadOnlyList<InkStroke> PriorStrokes
    {
        get => _prior.Strokes;
        set => _prior.Strokes = value;
    }

    public void Refresh() => _ink.Strokes = _session?.Strokes ?? [];

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        e.Handled = true;
        if (_session is not { IsFinished: false } session || _pointerId is not null)
        {
            return;
        }

        var point = e.GetCurrentPoint(this);
        if (point.PointerDeviceType == PointerDeviceType.Mouse && !point.Properties.IsLeftButtonPressed)
        {
            return;
        }

        if (!CapturePointer(e.Pointer))
        {
            return;
        }

        _feedback ??= App.Current.Services.GetService<IMarkingFeedback>();
        _pointerId = e.Pointer.PointerId;
        _lastPoint = Normalize(point.Position);
        _lastTimestamp = Environment.TickCount64;
        session.BeginStroke(_lastPoint);
        _feedback?.ScratchStart();
        Refresh();
        InkChanged?.Invoke(this, null);
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_pointerId != e.Pointer.PointerId || _session is not { } session)
        {
            return;
        }

        e.Handled = true;
        var point = Normalize(e.GetCurrentPoint(this).Position);
        if (!session.AddPoint(point))
        {
            return;
        }

        var now = Environment.TickCount64;
        var elapsed = Math.Max(MinFrameMilliseconds, now - _lastTimestamp);
        var distance = Math.Sqrt(Math.Pow(point.X - _lastPoint.X, 2) + Math.Pow(point.Y - _lastPoint.Y, 2));
        _feedback?.ScratchSpeed(distance / elapsed * FrameMilliseconds);
        _stillTimer.Stop();
        _stillTimer.Start();
        (_lastPoint, _lastTimestamp) = (point, now);

        _ink.RefreshLast();
        InkChanged?.Invoke(this, null);
    }

    private void OnPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_pointerId == e.Pointer.PointerId)
        {
            e.Handled = true;
            ReleasePointerCapture(e.Pointer);
            EndStroke();
        }
    }

    private void OnPointerCanceled(object sender, PointerRoutedEventArgs e)
    {
        if (_pointerId == e.Pointer.PointerId)
        {
            EndStroke();
        }
    }

    private void OnPointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        if (_pointerId == e.Pointer.PointerId)
        {
            EndStroke();
        }
    }

    // A tap on the square is a dot, never a dismiss of the overlay underneath.
    private void OnTapped(object sender, TappedRoutedEventArgs e)
    {
        e.Handled = true;
    }

    private void EndStroke()
    {
        if (_pointerId is null)
        {
            return;
        }

        _pointerId = null;
        _stillTimer.Stop();
        _feedback?.ScratchStop();
        _session?.EndStroke();
        InkChanged?.Invoke(this, null);
    }

    private void ForEachView(Action<InkView> apply)
    {
        apply(_prior);
        apply(_ink);
    }

    private InkPoint Normalize(Point position) => new(
        (float)Math.Clamp(position.X / Math.Max(1, ActualWidth), 0, 1),
        (float)Math.Clamp(position.Y / Math.Max(1, ActualHeight), 0, 1));
}
