using Microsoft.UI;
#if !HAS_UNO
using Microsoft.UI.System;
#endif
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
#if !HAS_UNO
    // AccessibilitySettings.HighContrastChanged needs a CoreWindow, which WinAppSDK windows don't have - subscribing throws.
    private ThemeSettings? _themeSettings;
#endif

    public ProgramHero()
    {
        InitializeComponent();
        UpdateBackdrop();
        Loaded += (_, _) =>
        {
            SubscribeHighContrastChanged();
            UpdateHighContrast();
        };
        Unloaded += (_, _) => UnsubscribeHighContrastChanged();
    }

#if HAS_UNO
    private void SubscribeHighContrastChanged() => _accessibility.HighContrastChanged += OnHighContrastChanged;

    private void UnsubscribeHighContrastChanged() => _accessibility.HighContrastChanged -= OnHighContrastChanged;
#else
    private void SubscribeHighContrastChanged()
    {
        if (_themeSettings is null && XamlRoot?.ContentIslandEnvironment is { } environment)
        {
            _themeSettings = ThemeSettings.CreateForWindowId(environment.AppWindowId);
            _themeSettings.Changed += OnHighContrastChanged;
        }
    }

    private void UnsubscribeHighContrastChanged()
    {
        if (_themeSettings is not null)
        {
            _themeSettings.Changed -= OnHighContrastChanged;
            _themeSettings = null;
        }
    }
#endif

    private void OnHighContrastChanged(object sender, object args) =>
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

        var gradient = AccentGradient();
        Backdrop.Background = gradient;
        if (string.IsNullOrEmpty(HeroImage))
        {
            return;
        }

        // Downloaded catalog art is a Storage URL: the gradient shows until it loads, and stays if it never does.
        if (Uri.TryCreate(HeroImage, UriKind.Absolute, out var remote) && (remote.Scheme == Uri.UriSchemeHttps || remote.Scheme == Uri.UriSchemeHttp))
        {
            string requested = HeroImage;
            ImageBrush brush = new() { ImageSource = new BitmapImage(remote), Stretch = Stretch.UniformToFill };
            brush.ImageOpened += (_, _) =>
            {
                if (HeroImage == requested)
                {
                    Backdrop.Background = brush;
                }
            };
            return;
        }

        Backdrop.Background = new ImageBrush
        {
            ImageSource = new BitmapImage(new Uri($"ms-appx:///{HeroImage.TrimStart('/')}")),
            Stretch = Stretch.UniformToFill,
        };
    }

    private LinearGradientBrush AccentGradient()
    {
        var accent = ParseColor(AccentColor);
        return new LinearGradientBrush
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

    internal static Color ParseColor(string hex)
    {
        string digits = hex.TrimStart('#');
        if (digits.Length == 6 && uint.TryParse(digits, System.Globalization.NumberStyles.HexNumber, null, out uint rgb))
        {
            return Color.FromArgb(255, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
        }

        return Color.FromArgb(255, 0x44, 0x4B, 0x52);
    }
}
