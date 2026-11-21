using AwesomeAssertions;
using ShowUp.Core.Tracking;

namespace ShowUp.Core.Tests.Tracking;

[TestClass]
public sealed class DurationParserTests
{
    [TestMethod]
    [DataRow("12:34", 754)]
    [DataRow("0:59", 59)]
    [DataRow("00:00", 0)]
    [DataRow(" 5:07 ", 307)]
    [DataRow("120:00", 7200)]
    public void DurationParser_Valid_mmss(string text, int expected)
    {
        DurationParser.TryParse(text, out var seconds).Should().BeTrue();

        seconds.Should().Be(expected);
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("12")]
    [DataRow("1:7")]
    [DataRow("1:60")]
    [DataRow("1:234")]
    [DataRow(":30")]
    [DataRow("1:2:3")]
    [DataRow("-1:30")]
    [DataRow("a:bc")]
    [DataRow("1000:00")]
    [DataRow("１:３０")]
    public void DurationParser_Invalid_ReturnsFalse(string? text)
    {
        DurationParser.TryParse(text, out var seconds).Should().BeFalse();

        seconds.Should().Be(0);
    }

    [TestMethod]
    [DataRow(754.0, "12:34")]
    [DataRow(5.0, "0:05")]
    [DataRow(-30.0, "-0:30")]
    [DataRow(0.0, "0:00")]
    public void Format_Seconds_ReadsAsMinutesAndSeconds(double seconds, string expected) =>
        DurationParser.Format(seconds).Should().Be(expected);
}
