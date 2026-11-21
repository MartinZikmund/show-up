using AwesomeAssertions;
using ShowUp.Core.Entitlements;

namespace ShowUp.Core.Tests.Entitlements;

[TestClass]
public sealed class FeatureGateTests
{
    private static readonly IReadOnlySet<string> ProIds = new HashSet<string> { "monthly", "yearly", "lifetime" };
    private static readonly DateTimeOffset Now = new(2026, 11, 21, 12, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void CanUse_FreeCustomSchedules_IsFalse() =>
        FeatureGate.CanUse(ProFeature.CustomSchedules, isPro: false).Should().BeFalse();

    [TestMethod]
    public void CanUse_ProEveryFeature_IsTrue()
    {
        foreach (var feature in Enum.GetValues<ProFeature>())
        {
            FeatureGate.CanUse(feature, isPro: true).Should().BeTrue(feature.ToString());
        }
    }

    [TestMethod]
    public void MaxActivePrograms_Free_IsOne() => FeatureGate.MaxActivePrograms(isPro: false).Should().Be(1);

    [TestMethod]
    public void MaxActivePrograms_Pro_IsUnlimited() => FeatureGate.MaxActivePrograms(isPro: true).Should().Be(int.MaxValue);

    [TestMethod]
    public void MaxReminders_FreeAndPro_AreOneAndTwenty()
    {
        FeatureGate.MaxReminders(isPro: false).Should().Be(1);
        FeatureGate.MaxReminders(isPro: true).Should().Be(20);
    }

    [TestMethod]
    public void StoreLicense_ActiveSubscription_IsPro() =>
        StoreLicenseMapper.IsPro([("monthly", true, Now.AddDays(10))], Now, ProIds).Should().BeTrue();

    [TestMethod]
    public void StoreLicense_ExpiredSubscription_IsFree() =>
        StoreLicenseMapper.IsPro([("monthly", true, Now.AddDays(-1))], Now, ProIds).Should().BeFalse();

    [TestMethod]
    public void StoreLicense_Lifetime_IsPro() =>
        StoreLicenseMapper.IsPro([("lifetime", true, null)], Now, ProIds).Should().BeTrue();

    [TestMethod]
    public void StoreLicense_InactiveOrUnknownAddOn_IsFree()
    {
        StoreLicenseMapper.IsPro([("lifetime", false, null)], Now, ProIds).Should().BeFalse();
        StoreLicenseMapper.IsPro([("other-addon", true, null)], Now, ProIds).Should().BeFalse();
        StoreLicenseMapper.IsPro([], Now, ProIds).Should().BeFalse();
    }
}
