using Windows.UI;

namespace ShowUp.Core.Marking;

/// <summary>Maps a <see cref="MarkerColor"/> to its ink on the paper (light) and on the chalkboard (dark).</summary>
public static class MarkerPalette
{
    /// <summary>Paper cell, the light theme's marker backdrop.</summary>
    public static readonly Color PaperCell = Color.FromArgb(255, 0xFF, 0xFD, 0xF7);

    /// <summary>Chalkboard cell, the dark theme's marker backdrop.</summary>
    public static readonly Color BoardCell = Color.FromArgb(255, 0x2B, 0x30, 0x35);

    // The house dust: chalk tinted toward the board, so the halo never reads as a grey rim.
    private static readonly Color AutoDarkBleed = Color.FromArgb(255, 0x6B, 0x6F, 0x72);
    private const double DarkBleedMix = 0.28;

    public static (Color Light, Color Dark) Resolve(MarkerColor color) => color switch
    {
        MarkerColor.Red => (Rgb(0xD9372B), Rgb(0xFF6B5E)),
        MarkerColor.Blue => (Rgb(0x2457C5), Rgb(0x7FA6FF)),
        MarkerColor.Green => (Rgb(0x1D8F4E), Rgb(0x5FD08E)),
        MarkerColor.Purple => (Rgb(0x7B3FC4), Rgb(0xC29BFF)),
        MarkerColor.Orange => (Rgb(0xE06A12), Rgb(0xFFA45C)),
        MarkerColor.Contrast => (Rgb(0x1F1F1F), Rgb(0xF3EFE4)),
        _ => (Rgb(0xD9372B), Rgb(0xF3EFE4)),
    };

    /// <summary>The wider under-stroke: the ink itself on paper, a board-tinted dust on the chalkboard.</summary>
    public static (Color Light, Color Dark) ResolveBleed(MarkerColor color)
    {
        var (light, dark) = Resolve(color);
        return (light, color == MarkerColor.Auto ? AutoDarkBleed : Mix(BoardCell, dark, DarkBleedMix));
    }

    private static Color Mix(Color from, Color to, double amount) => Color.FromArgb(
        255,
        (byte)Math.Round(from.R + ((to.R - from.R) * amount)),
        (byte)Math.Round(from.G + ((to.G - from.G) * amount)),
        (byte)Math.Round(from.B + ((to.B - from.B) * amount)));

    private static Color Rgb(int rgb) => Color.FromArgb(255, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
}
