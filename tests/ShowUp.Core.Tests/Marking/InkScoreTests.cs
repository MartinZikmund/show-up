using AwesomeAssertions;
using ShowUp.Core.Marking;

namespace ShowUp.Core.Tests.Marking;

[TestClass]
public class InkScoreTests
{
    [TestMethod]
    public void Counts_FullDiagonal_IsTrue()
    {
        InkStroke diagonal = Line(0.1f, 0.1f, 0.9f, 0.9f);

        InkScore.Counts([diagonal]).Should().BeTrue();
    }

    [TestMethod]
    public void Counts_ShortTick_IsFalse()
    {
        InkStroke tick = Stroke((0.4f, 0.5f), (0.45f, 0.56f), (0.56f, 0.4f));

        InkScore.Counts([tick]).Should().BeFalse();
    }

    [TestMethod]
    public void Counts_HorizontalLineAcross_IsFalse()
    {
        InkStroke line = Line(0.05f, 0.5f, 0.95f, 0.5f);

        InkScore.Of([line]).Should().BeApproximately(0.6, 1e-9);
        InkScore.Counts([line]).Should().BeFalse();
    }

    [TestMethod]
    public void Counts_TwoStrokeX_IsTrue()
    {
        InkStroke first = Line(0.2f, 0.2f, 0.8f, 0.8f);
        InkStroke second = Line(0.8f, 0.2f, 0.2f, 0.8f);

        InkScore.Counts([first, second]).Should().BeTrue();
    }

    [TestMethod]
    public void Counts_SmallX_NeedsBothStrokes()
    {
        InkStroke first = Line(0.28f, 0.28f, 0.72f, 0.72f);
        InkStroke second = Line(0.72f, 0.28f, 0.28f, 0.72f);

        InkScore.Counts([first]).Should().BeFalse();
        InkScore.Counts([first, second]).Should().BeTrue();
    }

    [TestMethod]
    public void Counts_Scribble_IsTrue()
    {
        List<InkPoint> points = [];
        for (var i = 0; i <= 12; i++)
        {
            points.Add(new(0.25f + i * 0.04f, i % 2 == 0 ? 0.25f : 0.75f));
        }

        InkScore.Counts([new InkStroke(points)]).Should().BeTrue();
    }

    [TestMethod]
    public void Of_NoInk_IsZero()
    {
        InkScore.Of([]).Should().Be(0);
    }

    [TestMethod]
    public void Of_ShortLine_MatchesPrototypeFormula()
    {
        // len 0.1 is below the 0.2 spread gate: only the length part scores.
        InkStroke line = Line(0.5f, 0.5f, 0.5f, 0.6f);

        InkScore.Of([line]).Should().BeApproximately(0.1 / 0.75 * 0.6, 1e-6);
    }

    internal static InkStroke Line(float x1, float y1, float x2, float y2, int segments = 20)
    {
        List<InkPoint> points = [];
        for (var i = 0; i <= segments; i++)
        {
            var t = (float)i / segments;
            points.Add(new(x1 + (x2 - x1) * t, y1 + (y2 - y1) * t));
        }

        return new(points);
    }

    private static InkStroke Stroke(params (float X, float Y)[] points) => new(points.Select(p => new InkPoint(p.X, p.Y)).ToList());
}
