using Windows.UI;

namespace ShowUp.Core.Marking;

/// <summary>WCAG 2.x contrast ratio between two opaque colors.</summary>
public static class ColorContrast
{
    public static double Ratio(Color a, Color b)
    {
        var (lighter, darker) = (Luminance(a), Luminance(b));
        if (lighter < darker)
        {
            (lighter, darker) = (darker, lighter);
        }

        return (lighter + 0.05) / (darker + 0.05);
    }

    private static double Luminance(Color color) =>
        (0.2126 * Linear(color.R)) + (0.7152 * Linear(color.G)) + (0.0722 * Linear(color.B));

    private static double Linear(byte channel)
    {
        var c = channel / 255.0;
        return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
    }
}
