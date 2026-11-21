using AwesomeAssertions;
using ShowUp.Core.Entitlements;

namespace ShowUp.Core.Tests.Entitlements;

[TestClass]
public sealed class StoreAccountGateTests
{
    [TestMethod]
    public async Task Switch_WhileARefreshRuns_WaitsForItToApply()
    {
        StoreAccountGate gate = new();
        TaskCompletionSource storeAnswer = new(TaskCreationOptions.RunContinuationsAsynchronously);
        List<string> log = [];
        var refresh = gate.RunAsync(async () =>
        {
            await storeAnswer.Task;
            log.Add("refresh applied for alice");
        });

        var switching = gate.SwitchAsync(() =>
        {
            log.Add("switched to bob");
            return Task.CompletedTask;
        });
        await Task.Delay(20);
        log.Should().BeEmpty("the switch must not slip in between the store's answer and applying it");
        storeAnswer.SetResult();
        await Task.WhenAll(refresh, switching);

        log.Should().Equal("refresh applied for alice", "switched to bob");
    }

    [TestMethod]
    public async Task IsCurrent_AfterASwitch_RejectsTheOlderGeneration()
    {
        StoreAccountGate gate = new();
        var purchaseStarted = gate.Generation;

        await gate.SwitchAsync(() => Task.CompletedTask);

        gate.IsCurrent(purchaseStarted).Should().BeFalse("a purchase that finished after the switch answered for the previous account");
        gate.IsCurrent(gate.Generation).Should().BeTrue();
    }

    [TestMethod]
    public async Task Switch_WorkThrows_ReleasesTheGate()
    {
        StoreAccountGate gate = new();

        var act = () => gate.SwitchAsync(() => throw new InvalidOperationException());
        await act.Should().ThrowAsync<InvalidOperationException>();

        await gate.RunAsync(() => Task.CompletedTask).WaitAsync(TimeSpan.FromSeconds(5));
    }
}
