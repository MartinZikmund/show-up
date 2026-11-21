using AwesomeAssertions;
using ShowUp.Core.Entitlements;
using ShowUp.Core.Marking;

namespace ShowUp.Core.Tests.Entitlements;

[TestClass]
public sealed class EntitlementHelpersTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 21, 12, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public async Task SingleFlight_OverlappingCalls_RunOnce()
    {
        SingleFlight flight = new();
        TaskCompletionSource gate = new();
        var runs = 0;
        Task Work()
        {
            runs++;
            return gate.Task;
        }

        var first = flight.RunAsync(Work);
        var second = flight.RunAsync(Work);
        gate.SetResult();
        await Task.WhenAll(first, second);
        runs.Should().Be(1);

        await flight.RunAsync(Work);
        runs.Should().Be(2);
    }

    [TestMethod]
    public void ProCache_ExpiredSubscription_IsFree() => ProCache.IsPro(true, Now.AddDays(-1), Now).Should().BeFalse();

    [TestMethod]
    public void ProCache_ActiveSubscription_IsPro() => ProCache.IsPro(true, Now.AddDays(3), Now).Should().BeTrue();

    [TestMethod]
    public void ProCache_LifetimeWithoutExpiry_IsPro() => ProCache.IsPro(true, null, Now).Should().BeTrue();

    [TestMethod]
    public void ProCache_NeverPro_IsFree() => ProCache.IsPro(false, null, Now).Should().BeFalse();

    [TestMethod]
    public void EffectiveMarkerColor_Lapsed_FallsBackToAuto() =>
        FeatureGate.EffectiveMarkerColor(MarkerColor.Purple, isPro: false).Should().Be(MarkerColor.Auto);

    [TestMethod]
    public void EffectiveMarkerColor_Pro_KeepsChoice() =>
        FeatureGate.EffectiveMarkerColor(MarkerColor.Purple, isPro: true).Should().Be(MarkerColor.Purple);
}
