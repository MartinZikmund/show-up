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
    public async Task SingleFlight_CallWhileRunning_GetsOneTrailingRun()
    {
        SingleFlight flight = new();
        TaskCompletionSource firstGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var runs = 0;
        async Task Work()
        {
            if (++runs == 1)
            {
                started.SetResult();
                await firstGate.Task;
            }
        }

        var first = flight.RunAsync(Work);
        await started.Task;
        var second = flight.RunAsync(Work);
        var third = flight.RunAsync(Work);
        firstGate.SetResult();
        await Task.WhenAll(first, second, third);

        runs.Should().Be(2, "callers arriving mid-run share one fresh run after it");
        second.Should().BeSameAs(third);
    }

    [TestMethod]
    [DataRow(true, false, true)]
    [DataRow(true, true, false)]
    [DataRow(false, false, false)]
    public void ProCache_IsLifetime_ProWithoutEndDate(bool isPro, bool hasExpiry, bool expected) =>
        ProCache.IsLifetime(isPro, hasExpiry ? Now.AddMonths(1) : null).Should().Be(expected);

    [TestMethod]
    public void ExpiryWatch_SubscriptionRunsOut_FiresOnce()
    {
        Microsoft.Extensions.Time.Testing.FakeTimeProvider time = new(Now);
        using ExpiryWatch watch = new(time);
        var fired = 0;

        watch.Schedule(Now.AddHours(2), () => Task.CompletedTask, () => false, () => fired++);
        time.Advance(TimeSpan.FromHours(1));
        fired.Should().Be(0);
        time.Advance(TimeSpan.FromHours(2));

        fired.Should().Be(1);
    }

    [TestMethod]
    public void ExpiryWatch_YearLongSubscription_StillFiresAtTheEnd()
    {
        Microsoft.Extensions.Time.Testing.FakeTimeProvider time = new(Now);
        using ExpiryWatch watch = new(time);
        var fired = 0;

        watch.Schedule(Now.AddDays(365), () => Task.CompletedTask, () => false, () => fired++);
        time.Advance(TimeSpan.FromDays(364));
        fired.Should().Be(0);
        time.Advance(TimeSpan.FromDays(2));

        fired.Should().Be(1);
    }

    [TestMethod]
    public void ExpiryWatch_Rescheduled_OnlyTheLatestFires()
    {
        Microsoft.Extensions.Time.Testing.FakeTimeProvider time = new(Now);
        using ExpiryWatch watch = new(time);
        List<string> fired = [];

        watch.Schedule(Now.AddHours(1), () => Task.CompletedTask, () => false, () => fired.Add("old"));
        watch.Schedule(null, () => Task.CompletedTask, () => false, () => fired.Add("none"));
        time.Advance(TimeSpan.FromHours(2));

        fired.Should().BeEmpty();
    }

    [TestMethod]
    public void ExpiryWatch_RenewedAtExpiry_StaysPro()
    {
        Microsoft.Extensions.Time.Testing.FakeTimeProvider time = new(Now);
        using ExpiryWatch watch = new(time);
        var renewed = false;
        var lapsed = 0;

        watch.Schedule(Now.AddHours(1), () => { renewed = true; return Task.CompletedTask; }, () => renewed, () => lapsed++);
        time.Advance(TimeSpan.FromHours(2));

        renewed.Should().BeTrue("the store is asked at the cached expiry");
        lapsed.Should().Be(0);
    }

    [TestMethod]
    public void ExpiryWatch_StoreUnreachableAtExpiry_Lapses()
    {
        Microsoft.Extensions.Time.Testing.FakeTimeProvider time = new(Now);
        using ExpiryWatch watch = new(time);
        var lapsed = 0;

        watch.Schedule(Now.AddHours(1), () => Task.FromException(new HttpRequestException("offline")), () => false, () => lapsed++);
        time.Advance(TimeSpan.FromHours(2));

        lapsed.Should().Be(1);
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
