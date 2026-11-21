using AwesomeAssertions;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging.Abstractions;
using ShowUp.Core.Catalog;
using ShowUp.Core.CustomPrograms;
using ShowUp.Core.Tests.CustomPrograms;

namespace ShowUp.Core.Tests.Catalog;

[TestClass]
public class CatalogServiceTests
{
    [TestMethod]
    public async Task GetCatalog_LoadsOnce_Caches()
    {
        FakeSource source = new([Fixture("sample-program.json")]);
        CatalogService service = new(source, new FakeCustomProgramRepository(), NullLogger<CatalogService>.Instance);

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
        CatalogService service = new(new FakeSource([bad, Fixture("sample-program.json")]), new FakeCustomProgramRepository(), NullLogger<CatalogService>.Instance);

        var catalog = await service.GetCatalogAsync();

        catalog.Programs.Select(p => p.Id).Should().Equal("sample");
    }

    [TestMethod]
    public async Task GetCatalog_MalformedProgram_IsSkipped()
    {
        CatalogService service = new(new FakeSource(["{ not json", Fixture("sample-program.json")]), new FakeCustomProgramRepository(), NullLogger<CatalogService>.Instance);

        var catalog = await service.GetCatalogAsync();

        catalog.Programs.Should().ContainSingle();
    }

    [TestMethod]
    public async Task GetProgram_UnknownId_ReturnsNull()
    {
        CatalogService service = new(new FakeSource([Fixture("sample-program.json")]), new FakeCustomProgramRepository(), NullLogger<CatalogService>.Instance);

        (await service.GetProgramAsync("missing")).Should().BeNull();
    }

    [TestMethod]
    public async Task GetProgram_RevisionNull_ReturnsHighestRevision()
    {
        var rev2 = Fixture("sample-program.json").Replace("\"revision\": 1", "\"revision\": 2");
        CatalogService service = new(new FakeSource([Fixture("sample-program.json"), rev2]), new FakeCustomProgramRepository(), NullLogger<CatalogService>.Instance);

        (await service.GetProgramAsync("sample"))!.Revision.Should().Be(2);
        (await service.GetProgramAsync("sample", 1))!.Revision.Should().Be(1);
        (await service.GetProgramAsync("sample", 3)).Should().BeNull();
    }

    [TestMethod]
    public async Task GetWorkout_KnownAndUnknown_ReturnsWorkoutOrNull()
    {
        CatalogService service = new(new FakeSource([]), new FakeCustomProgramRepository(), NullLogger<CatalogService>.Instance);

        (await service.GetWorkoutAsync("rest"))!.Id.Should().Be("rest");
        (await service.GetWorkoutAsync("missing")).Should().BeNull();
    }

    [TestMethod]
    public async Task GetTrackDays_SameTrack_ReturnsCachedInstance()
    {
        CatalogService service = new(new FakeSource([Fixture("sample-program.json")]), new FakeCustomProgramRepository(), NullLogger<CatalogService>.Instance);
        var track = (await service.GetProgramAsync("sample"))!.Tracks[0];

        service.GetTrackDays(track).Should().BeSameAs(service.GetTrackDays(track));
    }

    [TestMethod]
    public async Task CatalogService_IncludesCustomPrograms()
    {
        FakeCustomProgramRepository custom = new();
        var (id, revision) = await custom.SaveNewRevisionAsync(CustomJson("custom-a", "My plan"), "custom-a");
        CatalogService service = new(new FakeSource([Fixture("sample-program.json")]), custom, NullLogger<CatalogService>.Instance);

        var catalog = await service.GetCatalogAsync();

        var program = catalog.Programs.Should().ContainSingle(p => p.Id == id).Subject;
        program.Revision.Should().Be(revision);
        program.Verification.Should().Be(VerificationLevel.Custom);
        catalog.WorkoutsById.Should().ContainKey("custom-a-1");
        (await service.GetWorkoutAsync("custom-a-1"))!.Name.Should().Be("Jog");
        (await service.GetProgramAsync(id))!.Name.Should().Be("My plan");
    }

    [TestMethod]
    public async Task GetCatalog_AfterInvalidate_ReReadsCustomPrograms()
    {
        FakeCustomProgramRepository custom = new();
        CatalogService service = new(new FakeSource([]), custom, NullLogger<CatalogService>.Instance);
        await service.GetCatalogAsync();
        await custom.SaveNewRevisionAsync(CustomJson("custom-a", "Later"), "custom-a");

        (await service.GetCatalogAsync()).Programs.Should().BeEmpty();
        service.InvalidateCustomPrograms();

        (await service.GetCatalogAsync()).Programs.Should().ContainSingle(p => p.Name == "Later");
    }

    [TestMethod]
    public async Task Invalidate_ReReadsTheBundledCatalogAndCustomPrograms()
    {
        FakeCustomProgramRepository custom = new();
        FakeSource source = new([]);
        CatalogService service = new(source, custom, NullLogger<CatalogService>.Instance);
        await service.GetCatalogAsync();
        await custom.SaveNewRevisionAsync(CustomJson("custom-a", "Synced"), "custom-a");
        source.Programs = [Fixture("sample-program.json")];

        (await service.GetCatalogAsync()).Programs.Should().BeEmpty("cached");
        service.Invalidate();

        var catalog = await service.GetCatalogAsync();
        catalog.Programs.Select(p => p.Name).Should().Contain("Synced");
        catalog.Programs.Select(p => p.Id).Should().Contain("sample");
        source.WorkoutReads.Should().Be(2);
    }

    [TestMethod]
    public async Task DataChanged_InvalidatesLikeInvalidate()
    {
        FakeCustomProgramRepository custom = new();
        WeakReferenceMessenger messenger = new();
        CatalogService service = new(new FakeSource([]), custom, NullLogger<CatalogService>.Instance, messenger);
        await service.GetCatalogAsync();
        await custom.SaveNewRevisionAsync(CustomJson("custom-a", "Synced"), "custom-a");

        messenger.Send(new ShowUp.Core.Messages.DataChangedMessage());

        (await service.GetCatalogAsync()).Programs.Should().ContainSingle(p => p.Name == "Synced");
    }

    [TestMethod]
    public async Task Invalidate_WhileLoadingTheBundledCatalog_DoesNotCacheTheStaleRead()
    {
        FakeSource source = new([Fixture("sample-program.json")]) { Gate = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        CatalogService service = new(source, new FakeCustomProgramRepository(), NullLogger<CatalogService>.Instance);
        var loading = service.GetCatalogAsync();
        await source.Entered.Task;

        source.Programs = [];
        service.Invalidate();
        source.Gate.SetResult();
        await loading;
        source.Gate = null;

        (await service.GetCatalogAsync()).Programs.Should().BeEmpty();
    }

    [TestMethod]
    public async Task GetProgram_PinnedRevisionOfDeletedProgram_StillResolvesWithItsWorkouts()
    {
        FakeCustomProgramRepository custom = new();
        var (id, first) = await custom.SaveNewRevisionAsync(CustomJson("custom-a", "Old"), "custom-a");
        await custom.SaveNewRevisionAsync(CustomJson("custom-a", "New"), id);
        await custom.DeleteAsync(id);
        CatalogService service = new(new FakeSource([]), custom, NullLogger<CatalogService>.Instance);

        (await service.GetProgramAsync(id)).Should().BeNull();
        var pinned = await service.GetProgramAsync(id, first);

        pinned!.Name.Should().Be("Old");
        (await service.GetCatalogAsync()).WorkoutsById.Should().ContainKey("custom-a-1");
    }

    [TestMethod]
    public async Task GetCatalog_InvalidCustomProgram_IsSkipped()
    {
        FakeCustomProgramRepository custom = new();
        await custom.SaveNewRevisionAsync(CustomJson("custom-a", "Bad").Replace("custom-a-1", "nope"), "custom-a");
        await custom.SaveNewRevisionAsync("{ not json", "custom-b");
        CatalogService service = new(new FakeSource([]), custom, NullLogger<CatalogService>.Instance);

        (await service.GetCatalogAsync()).Programs.Should().BeEmpty();
    }

    [TestMethod]
    public async Task GetCatalog_CustomProgramUsesARemovedWorkout_LoadsWithAPlaceholder()
    {
        FakeCustomProgramRepository custom = new();
        await custom.SaveNewRevisionAsync(CustomJson("custom-a", "Mine").Replace("\"cardio\"", "\"removed-workout\""), "custom-a");
        CatalogService service = new(new FakeSource([]), custom, NullLogger<CatalogService>.Instance);

        var catalog = await service.GetCatalogAsync();

        catalog.Programs.Should().ContainSingle(p => p.Name == "Mine");
        catalog.WorkoutsById["removed-workout"].Should().Be(new Workout("removed-workout", "Unknown workout", null, WorkoutKind.Mixed, null));
    }

    [TestMethod]
    public async Task GetProgram_PinnedRevisionUsesARemovedWorkout_StillLoadsWithAPlaceholder()
    {
        FakeCustomProgramRepository custom = new();
        var (id, first) = await custom.SaveNewRevisionAsync(CustomJson("custom-a", "Old").Replace("\"cardio\"", "\"removed-workout\""), "custom-a");
        await custom.SaveNewRevisionAsync(CustomJson("custom-a", "New"), id);
        CatalogService service = new(new FakeSource([]), custom, NullLogger<CatalogService>.Instance);

        var pinned = await service.GetProgramAsync(id, first);

        pinned!.Name.Should().Be("Old");
        (await service.GetWorkoutAsync("removed-workout"))!.Name.Should().Be("Unknown workout");
    }

    [TestMethod]
    public async Task GetCatalog_BundledProgramMarkedCustom_IsSkipped()
    {
        var sneaky = Fixture("sample-program.json").Replace("\"verification\": \"official\"", "\"verification\": \"custom\"");
        CatalogService service = new(new FakeSource([sneaky]), new FakeCustomProgramRepository(), NullLogger<CatalogService>.Instance);

        (await service.GetCatalogAsync()).Programs.Should().BeEmpty();
    }

    [TestMethod]
    public async Task GetCatalog_InvalidatedWhileMerging_DoesNotCacheTheStaleMerge()
    {
        FakeCustomProgramRepository custom = new() { GetAllGate = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        CatalogService service = new(new FakeSource([]), custom, NullLogger<CatalogService>.Instance);
        var merging = service.GetCatalogAsync();
        await custom.GetAllEntered.Task;

        await custom.SaveNewRevisionAsync(CustomJson("custom-a", "Mid-merge"), "custom-a");
        service.InvalidateCustomPrograms();
        custom.GetAllGate.SetResult();
        (await merging).Programs.Should().BeEmpty("that merge read the repository before the save");
        custom.GetAllGate = null;

        (await service.GetCatalogAsync()).Programs.Should().ContainSingle(p => p.Name == "Mid-merge");
    }

    [TestMethod]
    public async Task GetCatalog_PinnedAndLatestRevision_ListEachCustomWorkoutOnce()
    {
        FakeCustomProgramRepository custom = new();
        var (id, first) = await custom.SaveNewRevisionAsync(CustomJson("custom-a", "Old"), "custom-a");
        await custom.SaveNewRevisionAsync(CustomJson("custom-a", "New").Replace("\"Jog\"", "\"Jog (renamed)\""), id);
        CatalogService service = new(new FakeSource([]), custom, NullLogger<CatalogService>.Instance);
        await service.GetProgramAsync(id, first);

        var catalog = await service.GetCatalogAsync();
        ShowUp.Core.ViewModels.BonusWorkoutDialogViewModel bonus = new(catalog.Workouts, new DateOnly(2026, 11, 21), new DateOnly(2026, 11, 21)) { Query = "jog" };

        catalog.Workouts.Should().ContainSingle(w => w.Id == "custom-a-1").Which.Name.Should().Be("Jog (renamed)");
        bonus.Suggestions.Should().ContainSingle();
    }

    internal static string CustomJson(string id, string name)
    {
        CustomProgramDraft draft = new(
            id,
            name,
            "#D9372B",
            [[[new CustomSession("cardio"), new CustomSession($"{id}-1", Optional: true)], [new CustomSession("rest")], [new CustomSession("rest")], [new CustomSession("rest")], [new CustomSession("rest")], [new CustomSession("rest")], [new CustomSession("rest")]]],
            [new Workout($"{id}-1", "Jog", 20, WorkoutKind.Cardio, null)]);
        return CustomProgramFactory.Serialize(CustomProgramFactory.Create(draft, new DateOnly(2026, 11, 21)));
    }

    private static string Fixture(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Catalog", "Fixtures", name));

    private sealed class FakeSource(IReadOnlyList<string> initialPrograms) : IBundledCatalogSource
    {
        public IReadOnlyList<string> Programs { get; set; } = initialPrograms;

        /// <summary>Holds the program read open until completed.</summary>
        public TaskCompletionSource? Gate { get; set; }

        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

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
            var snapshot = Programs;
            Entered.TrySetResult();
            if (Gate is { } gate)
            {
                await gate.Task;
            }

            await Task.Yield();
            return snapshot;
        }
    }
}
