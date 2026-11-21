using AwesomeAssertions;
using ShowUp.Core.Marking;

namespace ShowUp.Core.Tests.Marking;

[TestClass]
public class CrossMomentPolicyTests
{
    [TestMethod]
    public void ShouldShow_OnlyTodayPastDay_IsFalse() =>
        CrossMomentPolicy.ShouldShow(CrossMomentMode.OnlyToday, isToday: false).Should().BeFalse();

    [TestMethod]
    public void ShouldShow_OnlyTodayToday_IsTrue() =>
        CrossMomentPolicy.ShouldShow(CrossMomentMode.OnlyToday, isToday: true).Should().BeTrue();

    [TestMethod]
    public void ShouldShow_AlwaysPastDay_IsTrue() =>
        CrossMomentPolicy.ShouldShow(CrossMomentMode.Always, isToday: false).Should().BeTrue();

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void ShouldShow_Never_IsFalse(bool isToday) =>
        CrossMomentPolicy.ShouldShow(CrossMomentMode.Never, isToday).Should().BeFalse();
}
