using ShowUp.Core.Marking;

namespace ShowUp.Controls;

/// <summary>A round sample of a <see cref="MarkerColor"/>; Auto is split because it has no single color.</summary>
public sealed partial class MarkerSwatchDot : UserControl
{
    public static readonly DependencyProperty ColorProperty = DependencyProperty.Register(
        nameof(Color), typeof(MarkerColor), typeof(MarkerSwatchDot),
        new PropertyMetadata(MarkerColor.Auto, (d, _) => ((MarkerSwatchDot)d).UpdateFill()));

    public MarkerSwatchDot()
    {
        InitializeComponent();
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
        if (Color == MarkerColor.Auto)
        {
            DotFill.Color = dark;
            PaperHalfFill.Color = light;
            PaperHalf.Visibility = Visibility.Visible;
            return;
        }

        PaperHalf.Visibility = Visibility.Collapsed;
        DotFill.Color = ActualTheme == ElementTheme.Dark ? dark : light;
    }
}
