using AwesomeAssertions;
using Microsoft.Extensions.Time.Testing;
using ShowUp.Core.Marking;

namespace ShowUp.Core.Tests.Marking;

[TestClass]
public class InkSessionTests
{
    private FakeTimeProvider _time = null!;
    private InkSession _session = null!;
    private int _finished;

    [TestInitialize]
    public void Init()
    {
        _time = new();
        _session = new(_time);
        _finished = 0;
        _session.Finished += (_, _) => _finished++;
    }

    [TestCleanup]
    public void Cleanup() => _session.Dispose();

    [TestMethod]
    public void Settle_SecondStrokeWithinWindow_KeepsDrawing()
    {
        Draw(InkScoreTests.Line(0.2f, 0.2f, 0.8f, 0.8f));
        _session.Counts.Should().BeTrue();

        _time.Advance(TimeSpan.FromMilliseconds(500));
        _session.BeginStroke(new(0.8f, 0.2f));
        _time.Advance(TimeSpan.FromSeconds(2));

        _finished.Should().Be(0);
        _session.IsFinished.Should().BeFalse();
        _session.AddPoint(new(0.2f, 0.8f)).Should().BeTrue();
        _session.EndStroke();
        _time.Advance(InkSession.SettleDelay - TimeSpan.FromMilliseconds(1));
        _finished.Should().Be(0);
        _time.Advance(TimeSpan.FromMilliseconds(1));
        _finished.Should().Be(1);
        _session.Strokes.Should().HaveCount(2);
    }

    [TestMethod]
    public void Settle_PauseAfterThreshold_Finishes()
    {
        Draw(InkScoreTests.Line(0.1f, 0.1f, 0.9f, 0.9f));

        _time.Advance(InkSession.SettleDelay);

        _finished.Should().Be(1);
        _session.IsFinished.Should().BeTrue();
        _time.Advance(TimeSpan.FromSeconds(5));
        _finished.Should().Be(1);
    }

    [TestMethod]
    public void Settle_BelowThreshold_NeverFinishes()
    {
        Draw(InkScoreTests.Line(0.4f, 0.4f, 0.5f, 0.5f));

        _time.Advance(TimeSpan.FromSeconds(5));

        _finished.Should().Be(0);
    }

    [TestMethod]
    public void Undo_BelowThreshold_CancelsSettle()
    {
        Draw(InkScoreTests.Line(0.3f, 0.3f, 0.5f, 0.5f));
        Draw(InkScoreTests.Line(0.5f, 0.5f, 0.9f, 0.9f));
        _session.Counts.Should().BeTrue();

        _session.Undo();
        _time.Advance(TimeSpan.FromSeconds(5));

        _session.Counts.Should().BeFalse();
        _session.Strokes.Should().ContainSingle();
        _finished.Should().Be(0);
    }

    [TestMethod]
    public void AddPoint_WithinMinDistance_IsDropped()
    {
        _session.BeginStroke(new(0.5f, 0.5f));

        _session.AddPoint(new(0.502f, 0.5f)).Should().BeFalse();
        _session.AddPoint(new(0.51f, 0.5f)).Should().BeTrue();

        _session.Strokes[0].Points.Should().HaveCount(2);
    }

    [TestMethod]
    public void Finish_Done_FinishesOnceAndStopsDrawing()
    {
        Draw(InkScoreTests.Line(0.1f, 0.1f, 0.9f, 0.9f));

        _session.Finish().Should().BeTrue();
        _session.Finish().Should().BeFalse();
        _time.Advance(TimeSpan.FromSeconds(2));
        _session.BeginStroke(new(0.5f, 0.5f));

        _finished.Should().Be(1);
        _session.Strokes.Should().ContainSingle();
    }

    [TestMethod]
    public void Finish_BelowThreshold_DoesNothing()
    {
        Draw(InkScoreTests.Line(0.4f, 0.4f, 0.45f, 0.45f));

        _session.Finish().Should().BeFalse();

        _finished.Should().Be(0);
    }

    private void Draw(InkStroke stroke)
    {
        _session.BeginStroke(stroke.Points[0]);
        foreach (var point in stroke.Points.Skip(1))
        {
            _session.AddPoint(point);
        }

        _session.EndStroke();
    }
}
