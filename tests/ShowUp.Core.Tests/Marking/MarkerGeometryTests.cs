using AwesomeAssertions;
using ShowUp.Core.Marking;
using Windows.Foundation;

namespace ShowUp.Core.Tests.Marking;

[TestClass]
public class MarkerGeometryTests
{
    [TestMethod]
    public void Create_SameSeed_IsDeterministic()
    {
        var first = MarkerGeometry.Create(42, 96, 100);
        var second = MarkerGeometry.Create(42, 96, 100);

        second.Should().Be(first);
    }

    [TestMethod]
    public void Create_DifferentSeeds_Differ()
    {
        var first = MarkerGeometry.Create(1, 96, 96);
        var second = MarkerGeometry.Create(2, 96, 96);

        second.First.Should().NotBe(first.First);
        second.Second.Should().NotBe(first.Second);
    }

    [TestMethod]
    public void Create_Seed3_MatchesPrototype()
    {
        // Reference values from docs/prototypes/marker-cross.html strokePath with rng(3), 96x100.
        var (first, second) = MarkerGeometry.Create(3, 96, 100);

        AssertNear(first, (15.902086, 13.757748), (50.040410, 50.063081), (83.878702, 86.651123));
        AssertNear(second, (86.730323, 13.788073), (44.983195, 49.301687), (9.592082, 90.804341));
    }

    private static void AssertNear(MarkerStroke stroke, (double X, double Y) start, (double X, double Y) control, (double X, double Y) end)
    {
        stroke.Start.X.Should().BeApproximately(start.X, 1e-5);
        stroke.Start.Y.Should().BeApproximately(start.Y, 1e-5);
        stroke.Control.X.Should().BeApproximately(control.X, 1e-5);
        stroke.Control.Y.Should().BeApproximately(control.Y, 1e-5);
        stroke.End.X.Should().BeApproximately(end.X, 1e-5);
        stroke.End.Y.Should().BeApproximately(end.Y, 1e-5);
    }

    [TestMethod]
    [DataRow(1, 96.0, 96.0)]
    [DataRow(7, 40.0, 120.0)]
    [DataRow(123456, 360.0, 360.0)]
    [DataRow(int.MaxValue, 96.0, 104.0)]
    public void Create_StrokesStayNearCellBounds(int seed, double width, double height)
    {
        const double wobble = 4;
        const double overshoot = 8;
        var slack = overshoot + wobble;

        var (first, second) = MarkerGeometry.Create(seed, width, height, wobble, overshoot);

        foreach (var stroke in new[] { first, second })
        {
            for (var i = 0; i <= 20; i++)
            {
                var p = stroke.PointAt(i / 20.0);
                p.X.Should().BeInRange(-slack, width + slack);
                p.Y.Should().BeInRange(-slack, height + slack);
            }
        }
    }

    [TestMethod]
    public void Create_FirstStroke_GoesTopLeftToBottomRight()
    {
        var (first, second) = MarkerGeometry.Create(5, 96, 96);

        first.Start.X.Should().BeLessThan(first.End.X);
        first.Start.Y.Should().BeLessThan(first.End.Y);
        second.Start.X.Should().BeGreaterThan(second.End.X);
        second.Start.Y.Should().BeLessThan(second.End.Y);
    }

    [TestMethod]
    public void Length_StraightStroke_IsChordLength()
    {
        MarkerStroke stroke = new(new(0, 0), new(5, 5), new(10, 10));

        stroke.Length.Should().BeApproximately(Math.Sqrt(200), 0.01);
    }

    [TestMethod]
    public void Trim_Half_EndsAtCurveMidpoint()
    {
        MarkerStroke stroke = new(new(0, 0), new(10, 0), new(10, 10));

        var trimmed = stroke.Trim(0.5);

        trimmed.Start.Should().Be(stroke.Start);
        trimmed.End.Should().Be(stroke.PointAt(0.5));
    }

    [TestMethod]
    public void SeedFrom_SameGuid_SameSeed()
    {
        Guid id = new("6f9619ff-8b86-d011-b42d-00c04fc964ff");

        MarkerGeometry.SeedFrom(id).Should().Be(MarkerGeometry.SeedFrom(new Guid(id.ToString())));
    }

    [TestMethod]
    public void SeedFrom_KnownGuid_IsPinned()
    {
        // A change here would redraw every saved cross differently.
        MarkerGeometry.SeedFrom(new Guid("6f9619ff-8b86-d011-b42d-00c04fc964ff")).Should().Be(PinnedSeed);
    }

    [TestMethod]
    public void SeedFrom_AnyGuid_IsNonNegative()
    {
        MarkerGeometry.SeedFrom(Guid.Empty).Should().BeGreaterThanOrEqualTo(0);
        MarkerGeometry.SeedFrom(new Guid("ffffffff-ffff-ffff-ffff-ffffffffffff")).Should().BeGreaterThanOrEqualTo(0);
    }

    private const int PinnedSeed = 1112968054;
}
