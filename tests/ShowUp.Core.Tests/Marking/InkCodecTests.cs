using AwesomeAssertions;
using ShowUp.Core.Marking;

namespace ShowUp.Core.Tests.Marking;

[TestClass]
public class InkCodecTests
{
    [TestMethod]
    public void Codec_RoundTrip_WithinQuantization()
    {
        // Every point is a corner, so simplification keeps them all.
        InkStroke zigzag = new([new(0.1234f, 0.2f), new(0.3f, 0.8765f), new(0.5f, 0.2f), new(0.7f, 0.8f)]);
        InkStroke dot = new([new(0.5f, 0.5f)]);

        var decoded = InkCodec.Decode(InkCodec.Encode([zigzag, dot]));

        decoded.Should().HaveCount(2);
        decoded[0].Points.Should().HaveCount(4);
        for (var i = 0; i < 4; i++)
        {
            decoded[0].Points[i].X.Should().BeApproximately(zigzag.Points[i].X, 0.0005f);
            decoded[0].Points[i].Y.Should().BeApproximately(zigzag.Points[i].Y, 0.0005f);
        }

        decoded[1].Points.Should().ContainSingle().Which.Should().Be(new InkPoint(0.5f, 0.5f));
    }

    [TestMethod]
    public void Codec_StraightStroke_SimplifiesToItsEnds()
    {
        InkStroke line = InkScoreTests.Line(0.1f, 0.1f, 0.9f, 0.9f, segments: 50);

        var json = InkCodec.Encode([line]);

        json.Should().Be("[[0.1,0.1,0.9,0.9]]");
    }

    [TestMethod]
    public void Codec_LongStroke_CappedAt600Points()
    {
        List<InkPoint> points = [];
        for (var i = 0; i < 2000; i++)
        {
            points.Add(new(i / 2000f, i % 2 == 0 ? 0.1f : 0.9f));
        }

        var decoded = InkCodec.Decode(InkCodec.Encode([new InkStroke(points), InkScoreTests.Line(0.2f, 0.8f, 0.8f, 0.2f)]));

        decoded.Sum(s => s.Points.Count).Should().BeLessThanOrEqualTo(InkCodec.MaxPoints);
        decoded.Should().HaveCount(2);
        decoded[0].Points[0].Should().Be(new InkPoint(0, 0.1f));
        decoded[0].Points[^1].X.Should().BeApproximately(1999 / 2000f, 0.0005f);
        decoded[1].Points.Should().HaveCount(2);
    }

    [TestMethod]
    public void Codec_HandDrawnX_StaysUnder500Bytes()
    {
        InkStroke[] x = [Wobbly(0.15f, 0.15f, 0.85f, 0.85f, 1), Wobbly(0.85f, 0.15f, 0.15f, 0.85f, 2)];

        InkCodec.Encode(x).Length.Should().BeLessThan(500);
    }

    [TestMethod]
    public void Decode_Garbage_IsEmpty()
    {
        InkCodec.Decode("not json").Should().BeEmpty();
        InkCodec.Decode("").Should().BeEmpty();
        InkCodec.Decode("""{"a":1}""").Should().BeEmpty();
    }

    [TestMethod]
    public void Decode_OddCoordinateAndOutOfRange_IsTolerated()
    {
        var decoded = InkCodec.Decode("[[0.1,0.2,1.5,-1,0.3]]");

        decoded.Should().ContainSingle().Which.Points.Should().Equal(new InkPoint(0.1f, 0.2f), new InkPoint(1, 0));
    }

    // A finger stroke: a slight bow plus sub-pixel jitter, sampled densely.
    private static InkStroke Wobbly(float x1, float y1, float x2, float y2, int seed)
    {
        Random random = new(seed);
        List<InkPoint> points = [];
        for (var i = 0; i <= 120; i++)
        {
            var t = i / 120f;
            var bow = MathF.Sin(t * MathF.PI) * 0.03f;
            points.Add(new(
                x1 + (x2 - x1) * t + bow + (float)(random.NextDouble() - 0.5) * 0.004f,
                y1 + (y2 - y1) * t - bow + (float)(random.NextDouble() - 0.5) * 0.004f));
        }

        return new(points);
    }
}
