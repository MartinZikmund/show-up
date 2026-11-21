using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using ShowUp.Core.Entitlements;
using ShowUp.Core.Tests.Entitlements;
using ShowUp.Core.ViewModels;

namespace ShowUp.Core.Tests.ViewModels;

[TestClass]
public sealed class ProViewModelTests
{
    [TestMethod]
    public async Task Purchase_DoubleTap_PurchasesOnce()
    {
        FakeEntitlementService service = new() { PurchaseGate = new() };
        var viewModel = await LoadedAsync(service);
        var offer = viewModel.Offers[0];

        var first = viewModel.PurchaseCommand.ExecuteAsync(offer);
        var second = viewModel.PurchaseCommand.ExecuteAsync(offer);
        viewModel.PurchaseCommand.CanExecute(offer).Should().BeFalse();
        service.PurchaseGate.SetResult();
        await Task.WhenAll(first, second);

        service.PurchaseCalls.Should().Be(1);
        viewModel.IsBusy.Should().BeFalse();
    }

    [TestMethod]
    public async Task Purchase_Cancelled_KeepsFreeWithoutError()
    {
        FakeEntitlementService service = new() { Outcome = PurchaseOutcome.Cancelled };
        var viewModel = await LoadedAsync(service);

        await viewModel.PurchaseAsync(viewModel.Offers[0]);

        viewModel.IsPro.Should().BeFalse();
        viewModel.StateText.Should().BeEmpty();
        viewModel.IsBusy.Should().BeFalse();
    }

    [TestMethod]
    public async Task Purchase_Purchased_BecomesPro()
    {
        FakeEntitlementService service = new();
        var viewModel = await LoadedAsync(service);

        await viewModel.PurchaseAsync(viewModel.Offers[0]);

        viewModel.IsPro.Should().BeTrue();
        viewModel.StateText.Should().Be("ProStateActive");
    }

    [TestMethod]
    public async Task Purchase_Failed_ShowsError()
    {
        FakeEntitlementService service = new() { Outcome = PurchaseOutcome.Failed };
        var viewModel = await LoadedAsync(service);

        await viewModel.PurchaseAsync(viewModel.Offers[0]);

        viewModel.StateText.Should().Be("ProFailed");
    }

    [TestMethod]
    public async Task Offers_Unavailable_ShowsMessage()
    {
        FakeEntitlementService service = new() { PurchasesAvailable = false, Offers = [] };
        var viewModel = await LoadedAsync(service);

        viewModel.StateText.Should().Be("ProUnavailable");
        viewModel.CanBuy.Should().BeFalse();
        viewModel.RestoreCommand.CanExecute(null).Should().BeFalse();
    }

    [TestMethod]
    public async Task Restore_NothingFound_SaysSo()
    {
        FakeEntitlementService service = new() { RestoreResult = false };
        var viewModel = await LoadedAsync(service);

        await viewModel.RestoreAsync();

        viewModel.StateText.Should().Be("ProNothingToRestore");
    }

    [TestMethod]
    public async Task Entitlement_ChangesElsewhere_UpdatesState()
    {
        FakeEntitlementService service = new();
        var viewModel = await LoadedAsync(service);

        service.SetPro(true);

        viewModel.IsPro.Should().BeTrue();
        viewModel.CanBuy.Should().BeFalse();
    }

    [TestMethod]
    public void Features_AreLocalizedRows() => CreateViewModel(new FakeEntitlementService()).Features.Should().HaveCount(6);

    private static async Task<ProViewModel> LoadedAsync(FakeEntitlementService service)
    {
        var viewModel = CreateViewModel(service);
        await viewModel.LoadAsync();
        return viewModel;
    }

    private static ProViewModel CreateViewModel(FakeEntitlementService service) =>
        new(service, new FakeStringLocalizer(), new RecordingNavigationService(), new RecordingLauncher(), new ProLinks(new Uri("https://example.com/terms"), new Uri("https://example.com/privacy")), NullLogger<ProViewModel>.Instance);
}
