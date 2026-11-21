using AwesomeAssertions;
using ShowUp.Core.Marking;
using Windows.UI;

namespace ShowUp.Core.Tests.Marking;

[TestClass]
public sealed class MarkerPaletteTests
{
    private const double MinContrast = 3.0;

    public static IEnumerable<object[]> AllColors => Enum.GetValues<MarkerColor>().Select(c => new object[] { c });

    [TestMethod]
    [DynamicData(nameof(AllColors))]
    public void Resolve_AllColors_MeetContrastInBothThemes(MarkerColor color)
    {
        var (light, dark) = MarkerPalette.Resolve(color);

        ColorContrast.Ratio(light, MarkerPalette.PaperCell).Should().BeGreaterThanOrEqualTo(MinContrast);
        ColorContrast.Ratio(dark, MarkerPalette.BoardCell).Should().BeGreaterThanOrEqualTo(MinContrast);
    }

    [TestMethod]
    public void Resolve_Auto_IsRedOnPaperChalkOnBoard()
    {
        var (light, dark) = MarkerPalette.Resolve(MarkerColor.Auto);

        light.Should().Be(Color.FromArgb(255, 0xD9, 0x37, 0x2B));
        dark.Should().Be(Color.FromArgb(255, 0xF3, 0xEF, 0xE4));
    }

    [TestMethod]
    public void Resolve_Red_DiffersFromAutoOnlyOnTheBoard()
    {
        var auto = MarkerPalette.Resolve(MarkerColor.Auto);
        var red = MarkerPalette.Resolve(MarkerColor.Red);

        red.Light.Should().Be(auto.Light);
        red.Dark.Should().NotBe(auto.Dark);
    }

    [TestMethod]
    public void ResolveBleed_Auto_KeepsTheChalkDust()
    {
        var (light, dark) = MarkerPalette.ResolveBleed(MarkerColor.Auto);

        light.Should().Be(Color.FromArgb(255, 0xD9, 0x37, 0x2B));
        dark.Should().Be(Color.FromArgb(255, 0x6B, 0x6F, 0x72));
    }

    [TestMethod]
    [DynamicData(nameof(AllColors))]
    public void ResolveBleed_OnTheBoard_IsDarkerThanTheInk(MarkerColor color)
    {
        var (_, ink) = MarkerPalette.Resolve(color);
        var (_, bleed) = MarkerPalette.ResolveBleed(color);

        ColorContrast.Ratio(bleed, MarkerPalette.BoardCell).Should().BeLessThan(ColorContrast.Ratio(ink, MarkerPalette.BoardCell));
    }

    [TestMethod]
    public void Ratio_BlackOnWhite_Is21()
    {
        ColorContrast.Ratio(Color.FromArgb(255, 0, 0, 0), Color.FromArgb(255, 255, 255, 255)).Should().BeApproximately(21, 0.01);
    }
}
