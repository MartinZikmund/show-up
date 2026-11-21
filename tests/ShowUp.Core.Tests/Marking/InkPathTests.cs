using AwesomeAssertions;
using ShowUp.Core.Marking;
using Windows.Foundation;

namespace ShowUp.Core.Tests.Marking;

[TestClass]
public class InkPathTests
{
    [TestMethod]
    public void Smooth_ThreePoints_CurvesThroughTheMidpoint()
    {
        var path = InkPath.Smooth([new(0, 0), new(0.5f, 0), new(0.5f, 1)], 100, 200)!;

        path.Start.Should().Be(new Point(0, 0));
        path.Curves.Should().Equal((new Point(50, 0), new Point(50, 100)));
        path.End.Should().Be(new Point(50, 200));
    }

    [TestMethod]
    public void Smooth_SinglePoint_IsADot()
    {
        var path = InkPath.Smooth([new(0.5f, 0.5f)], 10, 10)!;

        path.Curves.Should().BeEmpty();
        path.End.Should().Be(path.Start);
    }

    [TestMethod]
    public void Smooth_NoPoints_IsNull()
    {
        InkPath.Smooth([], 10, 10).Should().BeNull();
    }
}
