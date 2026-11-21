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
    public async Task Purchase_Unverified_AsksToCheckBackOrRestore()
    {
        FakeEntitlementService service = new() { Outcome = PurchaseOutcome.Unverified };
        var viewModel = await LoadedAsync(service);

        await viewModel.PurchaseAsync(viewModel.Offers[0]);

        viewModel.StateText.Should().Be("ProCheckBack");
    }

    [TestMethod]
    public async Task Offers_StoreOffline_ShowsRetryNotUnavailable()
    {
        FakeEntitlementService service = new() { Offers = [], OffersLoadFailed = true };
        var viewModel = await LoadedAsync(service);

        viewModel.StateText.Should().Be("ProOffersFailed");
        viewModel.CanRetry.Should().BeTrue();
    }

    [TestMethod]
    public async Task OfferRows_HaveScreenReaderNameAndNote()
    {
        var viewModel = await LoadedAsync(new FakeEntitlementService());

        viewModel.OfferRows[0].AutomationName.Should().Be("Monthly, $2.99");
        viewModel.OfferRows[0].Note.Should().Be("ProOfferMonthlyNote");
    }

    [TestMethod]
    public async Task Disclosure_OnlyWithSubscriptionOffers()
    {
        FakeEntitlementService lifetimeOnly = new() { Offers = [new("lifetime", "Lifetime", "$19.99", ProOfferKind.Lifetime)] };

        (await LoadedAsync(new FakeEntitlementService())).ShowSubscriptionDisclosure.Should().BeTrue();
        (await LoadedAsync(lifetimeOnly)).ShowSubscriptionDisclosure.Should().BeFalse();
    }

    [TestMethod]
    public async Task ManageSubscription_Pro_OpensStoreUri()
    {
        FakeEntitlementService service = new() { IsPro = true, ManageUri = new("https://example.com/manage") };
        RecordingLauncher launcher = new();
        var viewModel = CreateViewModel(service, launcher);
        await viewModel.LoadAsync();

        viewModel.CanManageSubscription.Should().BeTrue();
        await viewModel.ManageSubscriptionCommand.ExecuteAsync(null);

        launcher.Launched.Should().Equal(new Uri("https://example.com/manage"));
    }

    [TestMethod]
    public async Task Links_TermsAndPrivacy_Launch()
    {
        RecordingLauncher launcher = new();
        var viewModel = CreateViewModel(new FakeEntitlementService(), launcher);

        await viewModel.OpenTermsCommand.ExecuteAsync(null);
        await viewModel.OpenPrivacyCommand.ExecuteAsync(null);

        launcher.Launched.Select(u => u.AbsolutePath).Should().Equal("/terms", "/privacy");
    }

    [TestMethod]
    public void Features_ReminderLimits_ComeFromFeatureGate()
    {
        var reminders = CreateViewModel(new FakeEntitlementService()).Features.Single(f => f.Title == "ProFeatureReminders");

        reminders.Free.Should().Contain(FeatureGate.MaxReminders(isPro: false).ToString());
        reminders.Pro.Should().Contain(FeatureGate.MaxReminders(isPro: true).ToString());
    }

    [TestMethod]
    public void Features_AreLocalizedRows() => CreateViewModel(new FakeEntitlementService()).Features.Should().HaveCount(6);

    private static async Task<ProViewModel> LoadedAsync(FakeEntitlementService service)
    {
        var viewModel = CreateViewModel(service);
        await viewModel.LoadAsync();
        return viewModel;
    }

    private static ProViewModel CreateViewModel(FakeEntitlementService service, RecordingLauncher? launcher = null) =>
        new(service, new FakeStringLocalizer(), new RecordingNavigationService(), launcher ?? new RecordingLauncher(), new ProLinks(new Uri("https://example.com/terms"), new Uri("https://example.com/privacy")), NullLogger<ProViewModel>.Instance);
}
