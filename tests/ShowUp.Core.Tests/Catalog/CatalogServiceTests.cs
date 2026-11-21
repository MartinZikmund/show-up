using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using ShowUp.Core.Catalog;

namespace ShowUp.Core.Tests.Catalog;

[TestClass]
public class CatalogServiceTests
{
    [TestMethod]
    public async Task GetCatalog_LoadsOnce_Caches()
    {
        FakeSource source = new([Fixture("sample-program.json")]);
        CatalogService service = new(source, NullLogger<CatalogService>.Instance);

        var results = await Task.WhenAll(service.GetCatalogAsync(), service.GetCatalogAsync(), service.GetCatalogAsync());
        var again = await service.GetCatalogAsync();

        source.WorkoutReads.Should().Be(1);
        source.ProgramReads.Should().Be(1);
        results.Should().OnlyContain(c => ReferenceEquals(c, again));
        again.Version.Should().Be(CatalogService.BundledCatalogVersion);
    }

    [TestMethod]
    public async Task GetCatalog_ProgramWithUnknownWorkout_IsSkipped()
    {
        var bad = Fixture("sample-program.json").Replace("\"id\": \"sample\"", "\"id\": \"bad\"").Replace("\"fit-test\"", "\"nope\"");
        CatalogService service = new(new FakeSource([bad, Fixture("sample-program.json")]), NullLogger<CatalogService>.Instance);

        var catalog = await service.GetCatalogAsync();

        catalog.Programs.Select(p => p.Id).Should().Equal("sample");
    }

    [TestMethod]
    public async Task GetCatalog_MalformedProgram_IsSkipped()
    {
        CatalogService service = new(new FakeSource(["{ not json", Fixture("sample-program.json")]), NullLogger<CatalogService>.Instance);

        var catalog = await service.GetCatalogAsync();

        catalog.Programs.Should().ContainSingle();
    }

    [TestMethod]
    public async Task GetProgram_UnknownId_ReturnsNull()
    {
        CatalogService service = new(new FakeSource([Fixture("sample-program.json")]), NullLogger<CatalogService>.Instance);

        (await service.GetProgramAsync("missing")).Should().BeNull();
    }

    [TestMethod]
    public async Task GetProgram_RevisionNull_ReturnsHighestRevision()
    {
        var rev2 = Fixture("sample-program.json").Replace("\"revision\": 1", "\"revision\": 2");
        CatalogService service = new(new FakeSource([Fixture("sample-program.json"), rev2]), NullLogger<CatalogService>.Instance);

        (await service.GetProgramAsync("sample"))!.Revision.Should().Be(2);
        (await service.GetProgramAsync("sample", 1))!.Revision.Should().Be(1);
        (await service.GetProgramAsync("sample", 3)).Should().BeNull();
    }

    [TestMethod]
    public async Task GetWorkout_KnownAndUnknown_ReturnsWorkoutOrNull()
    {
        CatalogService service = new(new FakeSource([]), NullLogger<CatalogService>.Instance);

        (await service.GetWorkoutAsync("rest"))!.Id.Should().Be("rest");
        (await service.GetWorkoutAsync("missing")).Should().BeNull();
    }

    [TestMethod]
    public async Task GetTrackDays_SameTrack_ReturnsCachedInstance()
    {
        CatalogService service = new(new FakeSource([Fixture("sample-program.json")]), NullLogger<CatalogService>.Instance);
        var track = (await service.GetProgramAsync("sample"))!.Tracks[0];

        service.GetTrackDays(track).Should().BeSameAs(service.GetTrackDays(track));
    }

    private static string Fixture(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Catalog", "Fixtures", name));

    private sealed class FakeSource(IReadOnlyList<string> programs) : IBundledCatalogSource
    {
        public int WorkoutReads { get; private set; }

        public int ProgramReads { get; private set; }

        public async Task<string> ReadWorkoutsAsync()
        {
            WorkoutReads++;
            await Task.Yield();
            return Fixture("sample-workouts.json");
        }

        public async Task<IReadOnlyList<string>> ReadProgramsAsync()
        {
            ProgramReads++;
            await Task.Yield();
            return programs;
        }
    }
}
