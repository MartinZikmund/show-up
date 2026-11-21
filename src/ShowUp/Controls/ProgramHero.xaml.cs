using Microsoft.UI;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.UI;
using Windows.UI.ViewManagement;

namespace ShowUp.Controls;

/// <summary>Hero art for a program; without an image, a gradient from its accent colour with the name on top.</summary>
public sealed partial class ProgramHero : UserControl
{
    public static readonly DependencyProperty ProgramNameProperty = Register(nameof(ProgramName), "", (d, _) => ((ProgramHero)d).UpdateFamilyVisibility());
    public static readonly DependencyProperty FamilyProperty = Register(nameof(Family), "", (d, _) => ((ProgramHero)d).UpdateFamilyVisibility());
    public static readonly DependencyProperty AccentColorProperty = Register(nameof(AccentColor), "", (d, _) => ((ProgramHero)d).UpdateBackdrop());
    public static readonly DependencyProperty HeroImageProperty = Register<string?>(nameof(HeroImage), null, (d, _) => ((ProgramHero)d).UpdateBackdrop());
    public static readonly DependencyProperty TitleFontSizeProperty = Register(nameof(TitleFontSize), 32d);

    private readonly AccessibilitySettings _accessibility = new();

    public ProgramHero()
    {
        InitializeComponent();
        UpdateBackdrop();
        Loaded += (_, _) =>
        {
            _accessibility.HighContrastChanged += Accessibility_HighContrastChanged;
            UpdateHighContrast();
        };
        Unloaded += (_, _) => _accessibility.HighContrastChanged -= Accessibility_HighContrastChanged;
    }

    private void Accessibility_HighContrastChanged(AccessibilitySettings sender, object args) =>
        DispatcherQueue.TryEnqueue(UpdateHighContrast);

    private void UpdateHighContrast() =>
        DecorLayers.Visibility = _accessibility.HighContrast ? Visibility.Collapsed : Visibility.Visible;

    public string ProgramName
    {
        get => (string)GetValue(ProgramNameProperty);
        set => SetValue(ProgramNameProperty, value);
    }

    public string Family
    {
        get => (string)GetValue(FamilyProperty);
        set => SetValue(FamilyProperty, value);
    }

    /// <summary>Hex colour such as "#C8102E".</summary>
    public string AccentColor
    {
        get => (string)GetValue(AccentColorProperty);
        set => SetValue(AccentColorProperty, value);
    }

    public string? HeroImage
    {
        get => (string?)GetValue(HeroImageProperty);
        set => SetValue(HeroImageProperty, value);
    }

    public double TitleFontSize
    {
        get => (double)GetValue(TitleFontSizeProperty);
        set => SetValue(TitleFontSizeProperty, value);
    }

    private static DependencyProperty Register<T>(string name, T defaultValue, PropertyChangedCallback? changed = null) =>
        DependencyProperty.Register(name, typeof(T), typeof(ProgramHero), new PropertyMetadata(defaultValue, changed));

    private void UpdateFamilyVisibility()
    {
        if (FamilyText is not null)
        {
            FamilyText.Visibility = string.Equals(Family, ProgramName, StringComparison.OrdinalIgnoreCase) ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    private void UpdateBackdrop()
    {
        if (Backdrop is null)
        {
            return;
        }

        if (!string.IsNullOrEmpty(HeroImage))
        {
            Backdrop.Background = new ImageBrush
            {
                ImageSource = new BitmapImage(new Uri($"ms-appx:///{HeroImage.TrimStart('/')}")),
                Stretch = Stretch.UniformToFill,
            };
            return;
        }

        var accent = ParseColor(AccentColor);
        Backdrop.Background = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 1),
            GradientStops =
            {
                new GradientStop { Offset = 0, Color = Mix(accent, Colors.Black, IsBright(accent) ? 0.6 : 0.45) },
                new GradientStop { Offset = 1, Color = Mix(accent, Colors.White, 0.15) },
            },
        };
    }

    private static bool IsBright(Color c) => (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255 > 0.6;

    private static Color Mix(Color from, Color to, double amount) => Color.FromArgb(
        255,
        (byte)(from.R + ((to.R - from.R) * amount)),
        (byte)(from.G + ((to.G - from.G) * amount)),
        (byte)(from.B + ((to.B - from.B) * amount)));

    private static Color ParseColor(string hex)
    {
        string digits = hex.TrimStart('#');
        if (digits.Length == 6 && uint.TryParse(digits, System.Globalization.NumberStyles.HexNumber, null, out uint rgb))
        {
            return Color.FromArgb(255, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
        }

        return Color.FromArgb(255, 0x44, 0x4B, 0x52);
    }
}
