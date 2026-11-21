using AwesomeAssertions;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using ShowUp.Core.Catalog;
using ShowUp.Core.CustomPrograms;
using ShowUp.Core.Messages;
using ShowUp.Core.Tests.Entitlements;
using ShowUp.Core.Tests.Tracking;

namespace ShowUp.Core.Tests.CustomPrograms;

[TestClass]
public sealed class CustomProgramServiceTests
{
    private const string Id = "custom-0b6a3d5e-1111-4c2a-9f00-000000000001";

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 21, 10, 0, 0, TimeSpan.Zero));
    private readonly FakeCustomProgramRepository _repository = new();
    private readonly FakeEntitlementService _entitlements = new() { IsPro = true };
    private readonly WeakReferenceMessenger _messenger = new();
    private readonly CatalogService _catalog;
    private readonly CustomProgramService _service;

    public CustomProgramServiceTests()
    {
        _time.SetLocalTimeZone(TimeZoneInfo.Utc);
        _catalog = new(new TestCatalogSource(), _repository, NullLogger<CatalogService>.Instance);
        _service = new(_repository, _catalog, _entitlements, _messenger, _time);
    }

    [TestMethod]
    public async Task Edit_BumpsRevision_EnrollmentKeepsOld()
    {
        var (_, first) = await _service.SaveAsync(Draft("First", "run"));
        FakeTrackingRepository tracking = new(_time);
        var enrollments = TestEnrollments.Create(tracking, _catalog, _messenger, entitlements: _entitlements);
        var original = (await _catalog.GetProgramAsync(Id))!;
        await enrollments.EnrollAsync(original, original.Tracks[0], new DateOnly(2026, 11, 21));

        var (_, second) = await _service.SaveAsync(Draft("Second", "abs"));

        second.Should().BeGreaterThan(first);
        (await _catalog.GetProgramAsync(Id))!.Name.Should().Be("Second");
        var reloaded = (await TestEnrollments.Create(tracking, _catalog, _messenger, entitlements: _entitlements).GetAllAsync()).Single();
        reloaded.Program.Revision.Should().Be(first);
        reloaded.Program.Name.Should().Be("First");
        reloaded.Track.Days[0].Sessions[0].WorkoutId.Should().Be("run");
    }

    [TestMethod]
    public async Task Save_OwnWorkout_IsInTheCatalogAfterwards()
    {
        Workout own = new($"{Id}-1", "Hill sprints", 25, WorkoutKind.Cardio, null);

        await _service.SaveAsync(Draft("Mine", own.Id) with { Workouts = [own] });

        (await _catalog.GetWorkoutAsync(own.Id)).Should().Be(own);
    }

    [TestMethod]
    public async Task Save_SendsTheChangedMessage()
    {
        List<string> changed = [];
        _messenger.Register<CustomProgramsChangedMessage>(this, (_, m) => changed.Add(m.ProgramId));

        await _service.SaveAsync(Draft("Mine", "run"));

        changed.Should().Equal(Id);
    }

    [TestMethod]
    public async Task Save_WithoutPro_ThrowsAndSavesNothing()
    {
        _entitlements.IsPro = false;

        var act = () => _service.SaveAsync(Draft("Mine", "run"));

        await act.Should().ThrowAsync<CustomProgramLockedException>();
        (await _repository.GetAllAsync()).Should().BeEmpty();
    }

    [TestMethod]
    public async Task Save_UnknownWorkout_Throws()
    {
        var act = () => _service.SaveAsync(Draft("Mine", "nope"));

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [TestMethod]
    public async Task Delete_HidesItButThePinnedRevisionResolves()
    {
        var (_, revision) = await _service.SaveAsync(Draft("Mine", "run"));
        await _catalog.GetCatalogAsync();

        await _service.DeleteAsync(Id);

        (await _catalog.GetCatalogAsync()).Programs.Should().NotContain(p => p.Id == Id);
        (await _catalog.GetProgramAsync(Id, revision))!.Name.Should().Be("Mine");
    }

    [TestMethod]
    public async Task GetVersionNumber_CountsRevisionsFromOne()
    {
        var (_, first) = await _service.SaveAsync(Draft("A", "run"));
        var (_, second) = await _service.SaveAsync(Draft("B", "run"));

        (await _service.GetVersionNumberAsync(Id, first)).Should().Be(1);
        (await _service.GetVersionNumberAsync(Id, second)).Should().Be(2);
    }

    [TestMethod]
    public async Task Enroll_CustomProgramWithoutPro_Throws()
    {
        await _service.SaveAsync(Draft("Mine", "run"));
        var program = (await _catalog.GetProgramAsync(Id))!;
        _entitlements.IsPro = false;
        var enrollments = TestEnrollments.Create(new FakeTrackingRepository(_time), _catalog, _messenger, entitlements: _entitlements);

        var act = () => enrollments.EnrollAsync(program, program.Tracks[0], new DateOnly(2026, 11, 21));

        await act.Should().ThrowAsync<CustomProgramLockedException>();
    }

    private static CustomProgramDraft Draft(string name, string firstWorkout) => new(
        Id,
        name,
        "#D9372B",
        [[[new CustomSession(firstWorkout)], .. Enumerable.Repeat<IReadOnlyList<CustomSession>>([new CustomSession("rest")], 6)]],
        []);

    private sealed class TestCatalogSource : IBundledCatalogSource
    {
        public Task<string> ReadWorkoutsAsync() => Task.FromResult(System.Text.Json.JsonSerializer.Serialize(TestCatalog.Workouts.ToArray(), CatalogJsonContext.Default.WorkoutArray));

        public Task<IReadOnlyList<string>> ReadProgramsAsync() => Task.FromResult<IReadOnlyList<string>>([]);
    }
}
