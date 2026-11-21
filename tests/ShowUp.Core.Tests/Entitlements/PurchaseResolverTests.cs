using AwesomeAssertions;
using ShowUp.Core.Entitlements;

namespace ShowUp.Core.Tests.Entitlements;

[TestClass]
public sealed class PurchaseResolverTests
{
    [TestMethod]
    public async Task Resolve_SuccessAndPro_IsPurchased() =>
        (await PurchaseResolver.ResolveAsync(BillingResult.Success, () => Task.FromResult(true))).Should().Be(PurchaseOutcome.Purchased);

    [TestMethod]
    public async Task Resolve_SuccessButNoEntitlement_IsUnverified() =>
        (await PurchaseResolver.ResolveAsync(BillingResult.Success, () => Task.FromResult(false))).Should().Be(PurchaseOutcome.Unverified);

    [TestMethod]
    public async Task Resolve_AlreadyOwned_RestoresThenPurchased()
    {
        var restores = 0;

        var outcome = await PurchaseResolver.ResolveAsync(BillingResult.AlreadyOwned, () =>
        {
            restores++;
            return Task.FromResult(true);
        });

        outcome.Should().Be(PurchaseOutcome.Purchased);
        restores.Should().Be(1);
    }

    [TestMethod]
    public async Task Resolve_NetworkOrServerAfterCheckout_IsUnconfirmed() =>
        (await PurchaseResolver.ResolveAsync(BillingResult.NetworkOrServer, () => Task.FromResult(false))).Should().Be(PurchaseOutcome.Unconfirmed);

    [TestMethod]
    public async Task Resolve_CancelledAndPending_DoNotRefresh()
    {
        var refreshed = false;
        Task<bool> Refresh()
        {
            refreshed = true;
            return Task.FromResult(true);
        }

        (await PurchaseResolver.ResolveAsync(BillingResult.Cancelled, Refresh)).Should().Be(PurchaseOutcome.Cancelled);
        (await PurchaseResolver.ResolveAsync(BillingResult.Pending, Refresh)).Should().Be(PurchaseOutcome.Pending);
        refreshed.Should().BeFalse();
    }

    [TestMethod]
    public async Task Resolve_OtherError_IsFailed() =>
        (await PurchaseResolver.ResolveAsync(BillingResult.Other, () => Task.FromResult(false))).Should().Be(PurchaseOutcome.Failed);
}
