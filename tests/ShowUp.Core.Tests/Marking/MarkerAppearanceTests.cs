using AwesomeAssertions;
using ShowUp.Core.Marking;
using ShowUp.Core.Tracking;

namespace ShowUp.Core.Tests.Marking;

[TestClass]
public class MarkerAppearanceTests
{
    [TestMethod]
    public void Complete_OneAutoOneDrawn_FullX()
    {
        var status = Day(sessions: 2, done: [0, 1]);

        MarkerAppearance.StrokesFor(status, new HashSet<int> { 0 }).Should().Be(2);
    }

    [TestMethod]
    public void Partial_TwoSessionsOneAuto_Slash()
    {
        var status = Day(sessions: 2, done: [0]);

        MarkerAppearance.StrokesFor(status, new HashSet<int> { 0 }).Should().Be(1);
    }

    [TestMethod]
    public void Partial_OneDrawn_NoStrokes()
    {
        var status = Day(sessions: 2, done: [0]);

        MarkerAppearance.StrokesFor(status, new HashSet<int>()).Should().Be(0);
    }

    [TestMethod]
    public void Complete_AllDrawn_NoStrokes()
    {
        var status = Day(sessions: 2, done: [0, 1]);

        MarkerAppearance.StrokesFor(status, new HashSet<int>()).Should().Be(0);
    }

    [TestMethod]
    public void Complete_SingleAutoSession_FullX()
    {
        MarkerAppearance.StrokesFor(Day(sessions: 1, done: [0]), new HashSet<int> { 0 }).Should().Be(2);
    }

    [TestMethod]
    public void Partial_ThreeSessionsOneAuto_TicksOnly()
    {
        MarkerAppearance.StrokesFor(Day(sessions: 3, done: [0]), new HashSet<int> { 0 }).Should().Be(0);
    }

    [TestMethod]
    public void StrokesFor_AutoSetHasUncrossedSession_IgnoresIt()
    {
        MarkerAppearance.StrokesFor(Day(sessions: 2, done: [0]), new HashSet<int> { 1 }).Should().Be(0);
    }

    [TestMethod]
    public void Outcomes_SecondSessionAfterAutoSlash_DrawnCompletesTheX()
    {
        var before = Day(sessions: 2, done: [0]);

        var outcomes = MarkerAppearance.Outcomes(before, new HashSet<int> { 0 }, sessionIndex: 1);

        outcomes.Should().Be((1, 2, 2));
    }

    [TestMethod]
    public void Outcomes_FirstSessionOfTwo_DrawnAddsNoStrokes()
    {
        var outcomes = MarkerAppearance.Outcomes(Day(sessions: 2, done: []), new HashSet<int>(), sessionIndex: 0);

        outcomes.Should().Be((0, 1, 0));
    }

    private static DayStatus Day(int sessions, int[] done) =>
        new(0, new DateOnly(2026, 12, 1), sessions, done.ToHashSet(), IsRestDay: false);
}
