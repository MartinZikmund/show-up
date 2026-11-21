using AwesomeAssertions;
using ShowUp.Core.Marking;

namespace ShowUp.Core.Tests.Marking;

[TestClass]
public class ScratchMixTests
{
    [TestMethod]
    public void From_Still_IsSilentAndLow()
    {
        var mix = ScratchMix.From(0);

        mix.Volume.Should().Be(0);
        mix.Rate.Should().Be(0.75);
    }

    [TestMethod]
    public void From_Fast_IsFullAndHigh()
    {
        var mix = ScratchMix.From(10);

        mix.Volume.Should().Be(1);
        mix.Rate.Should().BeApproximately(4000 / 2400.0, 1e-9);
    }

    [TestMethod]
    public void From_Moderate_FollowsThePrototypeGain()
    {
        // v = .2: gain .18 of .32, band 2600 Hz.
        var mix = ScratchMix.From(0.2);

        mix.Volume.Should().BeApproximately(0.18 / 0.32, 1e-9);
        mix.Rate.Should().BeApproximately(2600 / 2400.0, 1e-9);
    }

    [TestMethod]
    public void From_NotANumber_IsSilent()
    {
        ScratchMix.From(double.NaN).Volume.Should().Be(0);
    }
}
