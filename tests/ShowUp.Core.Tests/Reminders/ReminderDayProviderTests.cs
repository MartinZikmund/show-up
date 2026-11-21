using AwesomeAssertions;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Time.Testing;
using ShowUp.Core.Reminders;
using ShowUp.Core.Tests.Tracking;
using ShowUp.Core.Tests.ViewModels;
using ShowUp.Core.Tracking;

namespace ShowUp.Core.Tests.Reminders;

[TestClass]
public sealed class ReminderDayProviderTests
{
    private static readonly DateOnly _today = new(2026, 11, 21);

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 21, 10, 0, 0, TimeSpan.Zero));
    private readonly FakeCatalogService _catalog = new(
        TestCatalog.Program("p", 1, "run", "rest", "abs"),
        TestCatalog.Program("q", 1, "abs", "rest"),
        TestCatalog.Program("o", 1, "run?"));
    private readonly FakeTrackingRepository _repository;
    private readonly ActiveEnrollmentService _service;

    public ReminderDayProviderTests()
    {
        _time.SetLocalTimeZone(TimeZoneInfo.Utc);
        _repository = new(_time);
        _service = TestEnrollments.Create(_repository, _catalog, new WeakReferenceMessenger(), entitlements: _entitlements);
    }

    private readonly ShowUp.Core.Tests.Entitlements.FakeEntitlementService _entitlements = new() { IsPro = true };

    private async Task<Func<DateOnly, ReminderDay>> LoadAsync(int days = 3) =>
        await new ReminderDayProvider(new ScheduleQuery(_service, _catalog), _catalog, new FakeStringLocalizer()).LoadAsync(_today, days);

    [TestMethod]
    public async Task Load_NoEnrollments_NothingDueSoCompleted()
    {
        var day = await LoadAsync();

        day(_today).Completed.Should().BeTrue();
    }

    [TestMethod]
    public async Task Load_OpenWorkout_NamesIt()
    {
        await _repository.EnrollAsync("p", 1, "t", _today);

        var day = await LoadAsync();

        day(_today).Should().Be(new ReminderDay(false, "Run", false));
    }

    [TestMethod]
    public async Task Load_RestDay_IsRest()
    {
        await _repository.EnrollAsync("p", 1, "t", _today);

        var day = await LoadAsync();

        day(_today.AddDays(1)).Should().Be(new ReminderDay(false, null, true));
    }

    [TestMethod]
    public async Task Load_EverythingCrossed_Completed()
    {
        var enrollment = await _repository.EnrollAsync("p", 1, "t", _today);
        await _service.CrossAsync(enrollment.Id, 0, 0);

        var day = await LoadAsync();

        day(_today).Completed.Should().BeTrue();
    }

    [TestMethod]
    public async Task Load_TwoProgramsOpen_NamesTheFirstAndCountsTheRest()
    {
        await _repository.EnrollAsync("p", 1, "t", _today);
        _time.Advance(TimeSpan.FromSeconds(1));
        await _repository.EnrollAsync("q", 1, "t", _today);

        var day = await LoadAsync();

        day(_today).WorkoutTitle.Should().Be("ReminderMore_One(Abs,1)", "the selected, newest program comes first");
    }

    [TestMethod]
    public async Task Load_RestInOneProgramWorkoutInAnother_NamesTheWorkout()
    {
        await _repository.EnrollAsync("p", 1, "t", _today.AddDays(-1));
        _time.Advance(TimeSpan.FromSeconds(1));
        await _repository.EnrollAsync("q", 1, "t", _today);

        var day = await LoadAsync();

        day(_today).Should().Be(new ReminderDay(false, "Abs", false));
    }

    [TestMethod]
    public async Task Load_OnlyOptionalSessions_IsRest()
    {
        await _repository.EnrollAsync("o", 1, "t", _today);

        var day = await LoadAsync();

        day(_today).IsRest.Should().BeTrue();
    }

    [TestMethod]
    public async Task Load_LapsedNonPrimaryProgram_IsLeftOut()
    {
        var primary = await _repository.EnrollAsync("p", 1, "t", _today);
        _time.Advance(TimeSpan.FromSeconds(1));
        await _repository.EnrollAsync("q", 1, "t", _today);
        await _service.SetPrimaryAsync(primary.Id);
        _entitlements.SetPro(false);

        var day = await LoadAsync();

        day(_today).Should().Be(new ReminderDay(false, "Run", false), "the read-only program can't be crossed, so it can't be asked for");
    }
}
