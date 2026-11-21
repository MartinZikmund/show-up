using ShowUp.Core.Marking;
using Microsoft.UI.Xaml.Shapes;

namespace ShowUp.Controls;

/// <summary>A round sample of a <see cref="MarkerColor"/> as it looks in the current theme.</summary>
public sealed partial class MarkerSwatchDot : Grid
{
    public static readonly DependencyProperty ColorProperty = DependencyProperty.Register(
        nameof(Color), typeof(MarkerColor), typeof(MarkerSwatchDot),
        new PropertyMetadata(MarkerColor.Auto, (d, _) => ((MarkerSwatchDot)d).UpdateFill()));

    private readonly Ellipse _dot = new();
    private readonly SolidColorBrush _fill = new();

    public MarkerSwatchDot()
    {
        _dot.Fill = _fill;
        // A faint rim keeps Contrast (near black) and chalk swatches readable on both papers.
        _dot.Stroke = new SolidColorBrush(Windows.UI.Color.FromArgb(0x55, 0x80, 0x80, 0x80));
        _dot.StrokeThickness = 1;
        Children.Add(_dot);
        Loaded += (_, _) => UpdateFill();
        ActualThemeChanged += (_, _) => UpdateFill();
    }

    public MarkerColor Color
    {
        get => (MarkerColor)GetValue(ColorProperty);
        set => SetValue(ColorProperty, value);
    }

    private void UpdateFill()
    {
        var (light, dark) = MarkerPalette.Resolve(Color);
        _fill.Color = ActualTheme == ElementTheme.Dark ? dark : light;
    }
}
