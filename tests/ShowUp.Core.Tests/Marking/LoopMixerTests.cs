using AwesomeAssertions;
using ShowUp.Core.Marking;

namespace ShowUp.Core.Tests.Marking;

[TestClass]
public class LoopMixerTests
{
    [TestMethod]
    public void Fill_Silent_WritesZeros()
    {
        LoopMixer mixer = new([1000, -1000], sampleRate: 1000);
        var chunk = new short[8];

        mixer.Fill(chunk);

        chunk.Should().OnlyContain(s => s == 0);
    }

    [TestMethod]
    public void Fill_TargetRaised_EasesUpWithoutJumping()
    {
        LoopMixer mixer = new([10000], sampleRate: 1000, smoothingSeconds: 0.01) { TargetGain = 1 };
        var chunk = new short[100];

        mixer.Fill(chunk);

        chunk[0].Should().BeInRange(1, 2000);
        chunk.Should().BeInAscendingOrder();
        chunk[^1].Should().BeGreaterThan(9900);
    }

    [TestMethod]
    public void Fill_PastTheEnd_WrapsAroundTheLoop()
    {
        LoopMixer mixer = new([1, 2, 3], sampleRate: 1000, smoothingSeconds: 1e-9) { TargetGain = 1 };
        var chunk = new short[7];

        mixer.Fill(chunk);

        chunk.Should().Equal(1, 2, 3, 1, 2, 3, 1);
    }

    [TestMethod]
    public void TargetGain_OutOfRange_IsClamped()
    {
        LoopMixer mixer = new([1], sampleRate: 1000) { TargetGain = 4 };

        mixer.TargetGain.Should().Be(1);
    }
}
