using AwesomeAssertions;
using ShowUp.Core.Marking;
using ShowUp.Core.Tracking;

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

    [TestMethod]
    [DataRow(CrossMomentMode.Always)]
    [DataRow(CrossMomentMode.OnlyToday)]
    [DataRow(CrossMomentMode.Never)]
    public void ShouldShow_ProgramMilestone_AlwaysShows(CrossMomentMode mode) =>
        CrossMomentPolicy.ShouldShow(mode, isToday: false, new Milestone(MilestoneKind.Program, "Phase", 3)).Should().BeTrue();

    [TestMethod]
    [DataRow(MilestoneKind.Week)]
    [DataRow(MilestoneKind.Phase)]
    public void ShouldShow_WeekOrPhaseMilestone_ShowsOnAPastDayUnlessNever(MilestoneKind kind)
    {
        Milestone milestone = new(kind, "Phase", 1);

        CrossMomentPolicy.ShouldShow(CrossMomentMode.OnlyToday, isToday: false, milestone).Should().BeTrue();
        CrossMomentPolicy.ShouldShow(CrossMomentMode.Never, isToday: true, milestone).Should().BeFalse();
    }
}
