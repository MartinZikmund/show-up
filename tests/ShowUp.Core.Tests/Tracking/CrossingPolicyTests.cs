using AwesomeAssertions;
using ShowUp.Core.Tracking;

namespace ShowUp.Core.Tests.Tracking;

[TestClass]
public class CrossingPolicyTests
{
    [TestMethod]
    [DataRow("2026-12-01", "2026-12-01", true)]
    [DataRow("2026-11-01", "2026-12-01", true)]
    [DataRow("2026-12-02", "2026-12-01", false)]
    public void CanCross_ReturnsExpected(string day, string today, bool expected)
    {
        CrossingPolicy.CanCross(DateOnly.Parse(day), DateOnly.Parse(today)).Should().Be(expected);
    }
}
