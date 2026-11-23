using AwesomeAssertions;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Time.Testing;
using ShowUp.Core.Messages;
using ShowUp.Core.Storage;
using ShowUp.Core.Tests.Entitlements;
using ShowUp.Core.Tracking;

namespace ShowUp.Core.Tests.Tracking;

[TestClass]
public sealed class ActiveEnrollmentServiceTests
{
    private static readonly DateOnly _today = new(2026, 11, 21);

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 21, 10, 0, 0, TimeSpan.Zero));
    private readonly FakeCatalogService _catalog = new(
        TestCatalog.Program("p", 1, "run", "run", "run"),
        TestCatalog.Program("p", 2, "run", "run", "run", "run", "run"),
        TestCatalog.Program("q", 1, "abs", "abs"));

    private readonly WeakReferenceMessenger _messenger = new();
    private readonly FakeSyncedSettings _settings = new();
    private readonly ShowUp.Core.Tests.ViewModels.FakeAppPreferences _preferences = new();
    private readonly FakeEntitlementService _entitlements = new() { IsPro = true };
    private readonly FakeTrackingRepository _repository;
    private readonly ActiveEnrollmentService _service;

    public ActiveEnrollmentServiceTests()
    {
        _time.SetLocalTimeZone(TimeZoneInfo.Utc);
        _repository = new(_time);
        _service = TestEnrollments.Create(_repository, _catalog, _messenger, _settings, _entitlements, _preferences);
    }

    [TestMethod]
    public async Task GetSelected_NoEnrollment_ReturnsNull()
    {
        var active = await _service.GetSelectedAsync();

        active.Should().BeNull();
    }

    [TestMethod]
    public async Task GetAll_RepositoryWrittenBehindItsBack_IsStaleUntilInvalidated()
    {
        (await _service.GetAllAsync()).Should().BeEmpty();
        await _repository.EnrollAsync("p", 1, "t", _today);

        (await _service.GetAllAsync()).Should().BeEmpty("the snapshot is cached");
        _service.Invalidate();

        (await _service.GetAllAsync()).Should().ContainSingle();
    }

    [TestMethod]
    public async Task DataChanged_DropsTheSnapshot()
    {
        await _service.GetAllAsync();
        await _repository.EnrollAsync("p", 1, "t", _today);

        _messenger.Send(new DataChangedMessage());

        (await _service.GetAllAsync()).Should().ContainSingle();
    }

    [TestMethod]
    public async Task GetSelected_PinnedRevision_ResolvesThatRevision()
    {
        await _repository.EnrollAsync("p", 1, "t", _today);

        var active = await _service.GetSelectedAsync();

        active!.Program.Revision.Should().Be(1);
        active.Calendar.DayCount.Should().Be(3);
    }

    [TestMethod]
    public async Task GetSelected_PinnedRevisionMissing_FallsBackToLatest()
    {
        await _repository.EnrollAsync("p", 7, "t", _today);

        var active = await _service.GetSelectedAsync();

        active!.Program.Revision.Should().Be(2);
        active.Calendar.DayCount.Should().Be(5);
    }

    [TestMethod]
    public async Task CrossAsync_ShiftedSchedule_UsesCalendarDate()
    {
        var enrollment = await _repository.EnrollAsync("p", 1, "t", _today.AddDays(-5));
        await _repository.ShiftScheduleAsync(enrollment.Id, _today.AddDays(-4), 2, 3);

        await _service.CrossAsync(enrollment.Id, 1, 0);

        _repository.Crosses.Should().ContainSingle().Which.Date.Should().Be(_today.AddDays(-2));
    }

    [TestMethod]
    public async Task CrossAsync_WithInk_PersistsInk()
    {
        var enrollment = await _repository.EnrollAsync("p", 1, "t", _today);

        await _service.CrossAsync(enrollment.Id, 0, 0, "[[0,0,1,1]]");

        _repository.Crosses.Should().ContainSingle().Which.Ink.Should().Be("[[0,0,1,1]]");
    }

    [TestMethod]
    public async Task ShiftAsync_ActiveEnrollment_PassesTrackDayCount()
    {
        var enrollment = await _repository.EnrollAsync("p", 2, "t", _today);

        await _service.ShiftAsync(enrollment.Id, _today, 3);

        _repository.ShiftCalls.Should().Equal((_today, 3, 5));
        (await _service.GetAsync(enrollment.Id))!.Calendar.DateOf(0).Should().Be(_today.AddDays(3));
    }

    [TestMethod]
    public async Task CrossAsync_Crossed_SendsMessage()
    {
        var enrollment = await _repository.EnrollAsync("p", 1, "t", _today);
        var messages = 0;
        _messenger.Register<TrackingChangedMessage>(this, (_, _) => messages++);

        await _service.CrossAsync(enrollment.Id, 0, 0);

        messages.Should().Be(1);
    }

    [TestMethod]
    public async Task ReplaceAsync_Succeeds_SwitchesTheActiveEnrollment()
    {
        var old = await _repository.EnrollAsync("p", 1, "t", _today);
        var program = (await _catalog.GetProgramAsync("p", 2))!;

        var replaced = await _service.ReplaceAsync(old.Id, EnrollmentEndReason.Abandoned, program, program.Tracks[0], _today);

        replaced.Program.Revision.Should().Be(2);
        (await _service.GetAllAsync()).Select(e => e.Id).Should().Equal(replaced.Id);
        (await _service.GetSelectedAsync())!.Id.Should().Be(replaced.Id);
    }

    [TestMethod]
    public async Task ReplaceAsync_EnrollFails_OldEnrollmentStaysActive()
    {
        var old = await _repository.EnrollAsync("p", 1, "t", _today);
        var program = (await _catalog.GetProgramAsync("p", 2))!;
        _repository.FailEnroll = true;

        Func<Task> act = () => _service.ReplaceAsync(old.Id, EnrollmentEndReason.Abandoned, program, program.Tracks[0], _today);

        await act.Should().ThrowAsync<InvalidOperationException>();
        (await _service.GetSelectedAsync())!.Id.Should().Be(old.Id);
    }

    [TestMethod]
    public async Task ReplaceAsync_ReplacingThePrimary_NewOneBecomesPrimary()
    {
        var old = await _repository.EnrollAsync("p", 1, "t", _today);
        await _service.SetPrimaryAsync(old.Id);
        var program = (await _catalog.GetProgramAsync("q"))!;

        var replaced = await _service.ReplaceAsync(old.Id, EnrollmentEndReason.Abandoned, program, program.Tracks[0], _today);

        _settings.Values[ActiveEnrollmentService.PrimaryKey].Should().Be(replaced.Id.ToString("D"));
    }

    [TestMethod]
    public async Task EndAsync_ActiveEnrollment_LeavesNoActiveEnrollment()
    {
        var enrollment = await _repository.EnrollAsync("p", 1, "t", _today);

        await _service.EndAsync(enrollment.Id, EnrollmentEndReason.Abandoned);

        (await _service.GetSelectedAsync()).Should().BeNull();
    }

    [TestMethod]
    public async Task GetBonusWorkoutsAsync_BonusOutsideCalendar_IsExcluded()
    {
        var enrollment = await _repository.EnrollAsync("p", 1, "t", _today);
        await _service.LogBonusAsync(null, "Inside", _today, null);
        await _repository.LogBonusAsync(null, "Outside", _today.AddDays(-10), null);

        var bonuses = await _service.GetBonusWorkoutsAsync(enrollment.Id);

        bonuses.Select(b => b.Title).Should().Equal("Inside");
    }

    [TestMethod]
    public async Task Enroll_ProWithActive_StartsAlongsideAndSelectsIt()
    {
        var first = await EnrollAsync("p");

        var second = await EnrollAsync("q");

        (await _service.GetAllAsync()).Select(e => e.Id).Should().BeEquivalentTo([first.Id, second.Id]);
        (await _service.GetSelectedAsync())!.Id.Should().Be(second.Id);
    }

    [TestMethod]
    public async Task Enroll_FreeWithActive_Throws()
    {
        _entitlements.IsPro = false;
        await EnrollAsync("p");

        Func<Task> act = () => EnrollAsync("q");

        await act.Should().ThrowAsync<InvalidOperationException>();
        (await _service.GetAllAsync()).Should().ContainSingle();
    }

    [TestMethod]
    public async Task Cross_OnEnrollmentB_DoesNotTouchA()
    {
        var a = await EnrollAsync("p");
        var b = await EnrollAsync("q");

        await _service.CrossAsync(b.Id, 0, 0);

        (await _service.GetCompletionsAsync(b.Id)).Should().ContainSingle();
        (await _service.GetCompletionsAsync(a.Id)).Should().BeEmpty();
        _repository.CrossesByEnrollment.Should().Equal((b.Id, 0, 0));
    }

    [TestMethod]
    public async Task Select_PersistsPerDevice_SurvivesANewServiceButIsNotSynced()
    {
        var a = await EnrollAsync("p");
        await EnrollAsync("q");

        await _service.SelectAsync(a.Id);
        var reloaded = TestEnrollments.Create(_repository, _catalog, _messenger, _settings, _entitlements, _preferences);

        _preferences.SelectedEnrollmentId.Should().Be(a.Id);
        _settings.Values.Keys.Should().NotContain(k => k.Contains("selected"));

        (await reloaded.GetSelectedAsync())!.Id.Should().Be(a.Id);
        reloaded.SelectedEnrollmentId.Should().Be(a.Id);
    }

    [TestMethod]
    public async Task Select_EndedEnrollment_FallsBackToNewest()
    {
        var a = await EnrollAsync("p");
        var b = await EnrollAsync("q");
        await _service.SelectAsync(a.Id);

        await _service.EndAsync(a.Id, EnrollmentEndReason.Abandoned);

        (await _service.GetSelectedAsync())!.Id.Should().Be(b.Id);
    }

    [TestMethod]
    public async Task Lapse_TwoActive_OnlyPrimaryCrossable()
    {
        var a = await EnrollAsync("p");
        var b = await EnrollAsync("q");
        await _service.SetPrimaryAsync(a.Id);
        _entitlements.SetPro(false);

        await _service.CrossAsync(a.Id, 0, 0);
        Func<Task> crossB = () => _service.CrossAsync(b.Id, 0, 0);

        await crossB.Should().ThrowAsync<EnrollmentReadOnlyException>();
        _service.IsReadOnly((await _service.GetAsync(a.Id))!).Should().BeFalse();
        _service.IsReadOnly((await _service.GetAsync(b.Id))!).Should().BeTrue();
        _repository.CrossesByEnrollment.Should().Equal((a.Id, 0, 0));
    }

    [TestMethod]
    public async Task Lapse_NoPrimaryChosen_MostRecentlyStartedStaysWritable()
    {
        var a = await EnrollAsync("p");
        _time.Advance(TimeSpan.FromMinutes(1));
        var b = await EnrollAsync("q");
        _entitlements.SetPro(false);

        (await _service.GetPrimaryAsync())!.Id.Should().Be(b.Id);
        (await _service.HasPrimaryChoiceAsync()).Should().BeFalse();
        _service.IsReadOnly((await _service.GetAsync(a.Id))!).Should().BeTrue();
    }

    [TestMethod]
    public async Task Lapse_ProComesBack_NothingIsReadOnly()
    {
        var a = await EnrollAsync("p");
        await EnrollAsync("q");
        await _service.SetPrimaryAsync(a.Id);
        _entitlements.SetPro(false);

        _entitlements.SetPro(true);

        (await _service.GetAllAsync()).Should().OnlyContain(e => !_service.IsReadOnly(e));
    }

    [TestMethod]
    public async Task EntitlementChanged_SendsTrackingMessage()
    {
        var messages = 0;
        _messenger.Register<TrackingChangedMessage>(this, (_, _) => messages++);

        _entitlements.SetPro(false);

        messages.Should().Be(1);
    }

    [TestMethod]
    public async Task Replace_ReadOnlyEnrollment_Throws()
    {
        var a = await EnrollAsync("p");
        var b = await EnrollAsync("q");
        await _service.SetPrimaryAsync(a.Id);
        _entitlements.SetPro(false);
        var program = (await _catalog.GetProgramAsync("q"))!;

        Func<Task> act = () => _service.ReplaceAsync(b.Id, EnrollmentEndReason.Abandoned, program, program.Tracks[0], _today);

        await act.Should().ThrowAsync<EnrollmentReadOnlyException>();
        (await _service.GetAllAsync()).Select(e => e.Id).Should().Contain(b.Id);
    }

    [TestMethod]
    public async Task Finish_AllRequiredCrossed_EndsAsFinished()
    {
        var active = await EnrollAsync("q", daysAgo: 1);
        await _service.CrossAsync(active.Id, 0, 0);
        await _service.CrossAsync(active.Id, 1, 0);

        var finished = await _service.FinishAsync(active.Id);

        finished.Should().BeTrue();
        (await _service.GetAllAsync()).Should().BeEmpty();
        var ended = (await _service.GetHistoryAsync()).Should().ContainSingle().Subject.Enrollment;
        ended.EndReason.Should().Be(EnrollmentEndReason.Finished);
        ended.EndedAt.Should().NotBeNull();
    }

    [TestMethod]
    public async Task Finish_DayMissing_ReturnsFalseAndStaysActive()
    {
        var active = await EnrollAsync("q", daysAgo: 1);
        await _service.CrossAsync(active.Id, 1, 0);

        var finished = await _service.FinishAsync(active.Id);

        finished.Should().BeFalse();
        (await _service.GetAllAsync()).Should().ContainSingle().Which.Id.Should().Be(active.Id);
    }

    [TestMethod]
    public async Task Finish_ReadOnlyEnrollment_Throws()
    {
        var a = await EnrollAsync("p");
        var b = await EnrollAsync("q", daysAgo: 1);
        await _service.CrossAsync(b.Id, 0, 0);
        await _service.CrossAsync(b.Id, 1, 0);
        await _service.SetPrimaryAsync(a.Id);
        _entitlements.SetPro(false);

        Func<Task> act = () => _service.FinishAsync(b.Id);

        await act.Should().ThrowAsync<EnrollmentReadOnlyException>();
    }

    [TestMethod]
    public async Task Finish_SendsOneTrackingChangedMessage()
    {
        var active = await EnrollAsync("q", daysAgo: 1);
        await _service.CrossAsync(active.Id, 0, 0);
        await _service.CrossAsync(active.Id, 1, 0);
        var messages = 0;
        _messenger.Register<TrackingChangedMessage>(this, (_, _) => messages++);

        await _service.FinishAsync(active.Id);

        messages.Should().Be(1);
    }

    private async Task<ActiveEnrollment> EnrollAsync(string programId, int daysAgo = 0)
    {
        var program = (await _catalog.GetProgramAsync(programId))!;
        return await _service.EnrollAsync(program, program.Tracks[0], _today.AddDays(-daysAgo));
    }
}
