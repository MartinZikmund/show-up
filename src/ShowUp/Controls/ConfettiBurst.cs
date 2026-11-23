using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Shapes;

namespace ShowUp.Controls;

/// <summary>
/// A short fall of marker-coloured paper bits for a finished week, phase or program. Decoration only:
/// never hit-tested, never in the automation tree, and gone from the visual tree when it ends.
/// </summary>
public sealed partial class ConfettiBurst : Canvas
{
    private const int PieceCount = 36;
    private Storyboard? _storyboard;

    public static readonly DependencyProperty InkProperty = DependencyProperty.Register(
        nameof(Ink), typeof(Brush), typeof(ConfettiBurst), new PropertyMetadata(null));

    public ConfettiBurst()
    {
        IsHitTestVisible = false;
        AutomationProperties.SetAccessibilityView(this, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
    }

    public Brush? Ink
    {
        get => (Brush?)GetValue(InkProperty);
        set => SetValue(InkProperty, value);
    }

    /// <param name="seed">The same moment falls the same way every time.</param>
    public void Play(int seed)
    {
        Stop();
        double width = ActualWidth;
        double height = ActualHeight;
        if (width <= 0 || height <= 0 || Ink is not { } ink)
        {
            return;
        }

        Random random = new(seed);
        Storyboard storyboard = new();
        for (int i = 0; i < PieceCount; i++)
        {
            double size = 6 + (random.NextDouble() * 6);
            double rotation = random.Next(360);
            double opacity = 0.5 + (0.25 * random.Next(3));
            CompositeTransform transform = new() { Rotation = rotation };
            Rectangle piece = new()
            {
                Width = size,
                Height = size * 0.5,
                RadiusX = 1,
                RadiusY = 1,
                Fill = ink,
                Opacity = opacity,
                RenderTransform = transform,
                RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5),
            };
            SetLeft(piece, width * (0.1 + (0.8 * random.NextDouble())));
            SetTop(piece, -12);
            Children.Add(piece);

            // Longest piece: 250 ms wait + 1250 ms fall = 1.5 s.
            var begin = TimeSpan.FromMilliseconds(random.Next(250));
            var fall = TimeSpan.FromMilliseconds(800 + random.Next(450));
            Animate(storyboard, transform, nameof(CompositeTransform.TranslateY), 0, height * (0.5 + (0.45 * random.NextDouble())), begin, fall);
            Animate(storyboard, transform, nameof(CompositeTransform.TranslateX), 0, (random.NextDouble() - 0.5) * 120, begin, fall);
            Animate(storyboard, transform, nameof(CompositeTransform.Rotation), rotation, rotation + ((random.NextDouble() - 0.5) * 720), begin, fall);
            Animate(storyboard, piece, nameof(Opacity), opacity, 0, begin + (fall * 0.6), fall * 0.4);
        }

        storyboard.Completed += (_, _) => Stop();
        _storyboard = storyboard;
        storyboard.Begin();
    }

    public void Stop()
    {
        _storyboard?.Stop();
        _storyboard = null;
        Children.Clear();
    }

    private static void Animate(Storyboard storyboard, DependencyObject target, string property, double from, double to, TimeSpan begin, TimeSpan duration)
    {
        DoubleAnimation animation = new()
        {
            From = from,
            To = to,
            BeginTime = begin,
            Duration = new Duration(duration),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn },
        };
        Storyboard.SetTarget(animation, target);
        Storyboard.SetTargetProperty(animation, property);
        storyboard.Children.Add(animation);
    }
}
