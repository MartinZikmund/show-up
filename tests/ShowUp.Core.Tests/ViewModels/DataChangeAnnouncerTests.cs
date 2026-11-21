using AwesomeAssertions;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Time.Testing;
using ShowUp.Core.Messages;
using ShowUp.Core.Tests.Tracking;

namespace ShowUp.Core.Tests.ViewModels;

[TestClass]
public sealed class DataChangeAnnouncerTests
{
    [TestMethod]
    public async Task Announce_PageHearsItBeforeTheService_StillReadsFreshData()
    {
        FakeTimeProvider time = new(new DateTimeOffset(2026, 12, 10, 12, 0, 0, TimeSpan.Zero));
        WeakReferenceMessenger messenger = new();
        FakeTrackingRepository repository = new(time);
        FakeCatalogService catalog = new(TestCatalog.Program("p", 1, "run"));
        object page = new();
        Task<int>? pageRead = null;
        // Registered first, so it is not behind the service's own handler.
        ShowUp.Core.Tracking.ActiveEnrollmentService? service = null;
        messenger.Register<DataChangedMessage>(page, (_, _) => pageRead = ReadCountAsync());
        service = TestEnrollments.Create(repository, catalog, messenger);
        (await service.GetAllAsync()).Should().BeEmpty();
        await repository.EnrollAsync("p", 1, "t", new DateOnly(2026, 12, 1));

        new DataChangeAnnouncer(service, catalog, messenger).Announce();

        (await pageRead!).Should().Be(1);
        catalog.Invalidations.Should().BeGreaterThanOrEqualTo(1);

        async Task<int> ReadCountAsync() => (await service!.GetAllAsync()).Count;
    }
}
