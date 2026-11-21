using AwesomeAssertions;
using Microsoft.Extensions.Time.Testing;
using ShowUp.Core.Entitlements;

namespace ShowUp.Core.Tests.Entitlements;

[TestClass]
public sealed class RefreshThrottleTests
{
    private readonly FakeTimeProvider _time = new();
    private readonly RefreshThrottle _throttle;

    public RefreshThrottleTests()
    {
        _throttle = new(_time, RefreshThrottle.ActivationInterval);
    }

    [TestMethod]
    public void TryEnter_FirstCall_Passes() => _throttle.TryEnter().Should().BeTrue();

    [TestMethod]
    public void TryEnter_WithinTheInterval_IsRefused()
    {
        _throttle.TryEnter();
        _time.Advance(TimeSpan.FromSeconds(59));

        _throttle.TryEnter().Should().BeFalse();
    }

    [TestMethod]
    public void TryEnter_AfterTheInterval_PassesAgain()
    {
        _throttle.TryEnter();
        _time.Advance(TimeSpan.FromSeconds(60));

        _throttle.TryEnter().Should().BeTrue();
        _throttle.TryEnter().Should().BeFalse("the interval restarted");
    }
}
