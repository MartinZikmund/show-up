using Microsoft.UI.Xaml.Shapes;
using Path = Microsoft.UI.Xaml.Shapes.Path;
using ShowUp.Core.Marking;

namespace ShowUp.Controls;

/// <summary>
/// Renders user ink with the marker look: per stroke a 1.35× bleed (all bleeds underneath) and the main line at 90%,
/// width <c>9 × max(0.6, min(w, h) / 96)</c>, smoothed with midpoint quadratics (<see cref="InkPath"/>).
/// </summary>
public sealed partial class InkView : Grid
{
    private const double BaseThickness = 9;
    private const double ReferenceCellSize = 96;
    private const double UnderStrokeWidthRatio = 1.35;
    private const double MainOpacity = 0.9;

    public static readonly DependencyProperty MarkerBrushProperty = DependencyProperty.Register(
        nameof(MarkerBrush), typeof(Brush), typeof(InkView),
        new PropertyMetadata(null, (d, _) => ((InkView)d).Restyle()));

    public static readonly DependencyProperty BleedOpacityProperty = DependencyProperty.Register(
        nameof(BleedOpacity), typeof(double), typeof(InkView),
        new PropertyMetadata(0.35, (d, _) => ((InkView)d).Restyle()));

    public static readonly DependencyProperty InkOpacityProperty = DependencyProperty.Register(
        nameof(InkOpacity), typeof(double), typeof(InkView),
        new PropertyMetadata(1.0, (d, _) => ((InkView)d).Restyle()));

    private readonly Canvas _under = new();
    private readonly Canvas _main = new();
    private readonly List<(Path Under, Path Main)> _paths = [];
    private IReadOnlyList<InkStroke> _strokes = [];

    public InkView()
    {
        IsHitTestVisible = false;
        Children.Add(_under);
        Children.Add(_main);
        SizeChanged += (_, _) => Redraw();
    }

    public Brush? MarkerBrush
    {
        get => (Brush?)GetValue(MarkerBrushProperty);
        set => SetValue(MarkerBrushProperty, value);
    }

    /// <summary>The bleed's opacity: 0.35 on paper, lower on the chalkboard (<c>MarkerBleedOpacity</c>).</summary>
    public double BleedOpacity
    {
        get => (double)GetValue(BleedOpacityProperty);
        set => SetValue(BleedOpacityProperty, value);
    }

    /// <summary>Multiplies both layers: 0.5 for the day's earlier ink in the draw moment.</summary>
    public double InkOpacity
    {
        get => (double)GetValue(InkOpacityProperty);
        set => SetValue(InkOpacityProperty, value);
    }

    public IReadOnlyList<InkStroke> Strokes
    {
        get => _strokes;
        set
        {
            _strokes = value ?? [];
            Redraw();
        }
    }

    /// <summary>Cheap live update while a stroke grows: syncs the path count and redraws only the last stroke.</summary>
    public void RefreshLast()
    {
        if (_paths.Count != _strokes.Count || _strokes.Count == 0)
        {
            Redraw();
            return;
        }

        SetGeometry(_strokes.Count - 1, Thickness);
    }

    private double Thickness => BaseThickness * Math.Max(0.6, Math.Min(ActualWidth, ActualHeight) / ReferenceCellSize);

    private void Redraw()
    {
        while (_paths.Count > _strokes.Count)
        {
            var (under, main) = _paths[^1];
            _under.Children.Remove(under);
            _main.Children.Remove(main);
            _paths.RemoveAt(_paths.Count - 1);
        }

        while (_paths.Count < _strokes.Count)
        {
            Path under = CreatePath(), main = CreatePath();
            _under.Children.Add(under);
            _main.Children.Add(main);
            _paths.Add((under, main));
        }

        Restyle();
        var thickness = Thickness;
        for (var i = 0; i < _strokes.Count; i++)
        {
            SetGeometry(i, thickness);
        }
    }

    private void Restyle()
    {
        foreach (var (under, main) in _paths)
        {
            under.Stroke = MarkerBrush;
            main.Stroke = MarkerBrush;
            under.Opacity = BleedOpacity * InkOpacity;
            main.Opacity = MainOpacity * InkOpacity;
        }
    }

    private void SetGeometry(int index, double thickness)
    {
        var (under, main) = _paths[index];
        if (ActualWidth <= 0 || ActualHeight <= 0 || InkPath.Smooth(_strokes[index].Points, ActualWidth, ActualHeight) is not { } path)
        {
            under.Data = null;
            main.Data = null;
            return;
        }

        // Geometry can't be shared between two Paths, so each gets its own.
        under.Data = ToGeometry(path);
        main.Data = ToGeometry(path);
        under.StrokeThickness = thickness * UnderStrokeWidthRatio;
        main.StrokeThickness = thickness;
    }

    private static Path CreatePath() => new()
    {
        StrokeStartLineCap = PenLineCap.Round,
        StrokeEndLineCap = PenLineCap.Round,
        StrokeLineJoin = PenLineJoin.Round,
    };

    private static PathGeometry ToGeometry(InkPath path)
    {
        PathFigure figure = new() { StartPoint = path.Start, IsClosed = false, IsFilled = false };
        foreach (var (control, end) in path.Curves)
        {
            figure.Segments.Add(new QuadraticBezierSegment { Point1 = control, Point2 = end });
        }

        figure.Segments.Add(new LineSegment { Point = path.End });
        PathGeometry geometry = new();
        geometry.Figures.Add(figure);
        return geometry;
    }
}
