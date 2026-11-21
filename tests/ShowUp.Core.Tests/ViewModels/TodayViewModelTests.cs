using AwesomeAssertions;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using ShowUp.Core.Catalog;
using ShowUp.Core.Marking;
using ShowUp.Core.Tests.Tracking;
using ShowUp.Core.Tracking;
using ShowUp.Core.ViewModels;
using ShowUp.Services.Dialogs;
using ShowUp.Services.Settings;

namespace ShowUp.Core.Tests.ViewModels;

[TestClass]
public sealed class TodayViewModelTests
{
    private static readonly DateOnly _today = new(2026, 11, 21);

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 21, 0, 0, 0, TimeSpan.Zero));
    private readonly FakeTrackingRepository _repository;
    private readonly FakeAppPreferences _preferences = new() { CrossOffStyle = CrossOffStyle.AutoMarker, CrossMomentMode = CrossMomentMode.Never };
    private readonly RecordingJournalDialogs _journalDialogs = new();
    private readonly FakeCrossMoment _crossMoment = new();
    private readonly FakeConfirmationDialogService _confirmation = new();
    private readonly FakeBonusWorkoutDialog _bonusDialog = new();
    private readonly FakeSyncedSettings _settings = new();
    private ActiveEnrollmentService? _service;
    private readonly ShowUp.Core.Tests.Entitlements.FakeEntitlementService _entitlements = new() { IsPro = true };

    public TodayViewModelTests()
    {
        _time.SetLocalTimeZone(TimeZoneInfo.Utc);
        _repository = new(_time);
    }

    [TestMethod]
    public async Task Load_RestDay_ShowsRestState()
    {
        var viewModel = await LoadAsync(_today, "rest");

        viewModel.Programs[0].IsRestDay.Should().BeTrue();
        viewModel.Programs[0].DayNumber.Should().Be(1);
        viewModel.Programs[0].Sessions.Should().ContainSingle();
    }

    [TestMethod]
    public async Task Load_TrainingDay_ListsSessionsWithDetails()
    {
        var viewModel = await LoadAsync(_today.AddDays(-2), "run", "run", "run+abs");

        viewModel.Programs[0].IsRestDay.Should().BeFalse();
        viewModel.Programs[0].DayNumber.Should().Be(3);
        viewModel.Programs[0].Sessions.Select(s => s.Name).Should().Equal("Run", "Abs");
        viewModel.Programs[0].Sessions[0].DurationMinutes.Should().Be(30);
        viewModel.Programs[0].Sessions[0].Kind.Should().Be(WorkoutKind.Cardio);
    }

    [TestMethod]
    public async Task Load_ProgramNotStarted_IsOffSchedule()
    {
        var viewModel = await LoadAsync(_today.AddDays(3), "run");

        viewModel.HasEnrollment.Should().BeTrue();
        viewModel.Programs[0].IsOffSchedule.Should().BeTrue();
        viewModel.Programs[0].Sessions.Should().BeEmpty();
    }

    [TestMethod]
    public async Task Load_NoEnrollment_ShowsInvitation()
    {
        var viewModel = CreateViewModel(new FakeCatalogService());

        await viewModel.LoadAsync();

        viewModel.ShowNoEnrollment.Should().BeTrue();
        viewModel.HasEnrollment.Should().BeFalse();
    }

    [TestMethod]
    [DataRow(6, "GreetingMorning")]
    [DataRow(14, "GreetingAfternoon")]
    [DataRow(20, "GreetingEvening")]
    [DataRow(2, "GreetingNight")]
    public async Task Load_HourOfDay_PicksGreeting(int hour, string expected)
    {
        _time.SetUtcNow(new DateTimeOffset(2026, 11, 21, hour, 0, 0, TimeSpan.Zero));
        var viewModel = CreateViewModel(new FakeCatalogService());

        await viewModel.LoadAsync();

        viewModel.Greeting.Should().Be(expected);
    }

    [TestMethod]
    public async Task Cross_FromToday_CrossesCorrectSession()
    {
        var viewModel = await LoadAsync(_today.AddDays(-1), "run", "run+abs");

        await viewModel.Programs[0].Sessions[1].CrossCommand.ExecuteAsync(null);

        var cross = _repository.Crosses.Should().ContainSingle().Subject;
        (cross.Day, cross.Session, cross.Date).Should().Be((1, 1, _today));
        viewModel.Programs[0].Sessions[1].IsDone.Should().BeTrue();
        viewModel.Programs[0].Sessions[0].IsDone.Should().BeFalse();
        viewModel.Programs[0].MarkStrokes.Should().Be(1);
    }

    [TestMethod]
    public async Task Cross_LastSession_CompletesDayAndStreak()
    {
        var viewModel = await LoadAsync(_today, "run");

        await viewModel.Programs[0].Sessions[0].CrossCommand.ExecuteAsync(null);

        viewModel.Programs[0].MarkStrokes.Should().Be(2);
        viewModel.Programs[0].CurrentStreak.Should().Be(1);
    }

    [TestMethod]
    public async Task Cross_TodayWithMomentAlways_ShowsCrossMoment()
    {
        _preferences.CrossMomentMode = CrossMomentMode.Always;
        var viewModel = await LoadAsync(_today, "run");

        await viewModel.Programs[0].Sessions[0].CrossCommand.ExecuteAsync(null);

        _crossMoment.Shown.Should().ContainSingle().Which.Title.Should().Be("Run");
    }

    [TestMethod]
    public async Task Cross_DrawnCross_PersistsInk()
    {
        _preferences.CrossOffStyle = CrossOffStyle.DrawYourself;
        _crossMoment.DrawResult = CrossMomentResult.Drawn("[[0,0,1,1]]");
        var viewModel = await LoadAsync(_today, "run");

        await viewModel.Programs[0].Sessions[0].CrossCommand.ExecuteAsync(null);

        _repository.Crosses.Should().ContainSingle().Which.Ink.Should().Be("[[0,0,1,1]]");
        viewModel.Programs[0].Inks.Should().ContainSingle();
    }

    [TestMethod]
    public async Task Cross_DrawCancelled_DoesNotWrite()
    {
        _preferences.CrossOffStyle = CrossOffStyle.DrawYourself;
        _crossMoment.DrawResult = CrossMomentResult.Cancelled;
        var viewModel = await LoadAsync(_today, "run");

        await viewModel.Programs[0].Sessions[0].CrossCommand.ExecuteAsync(null);

        _repository.Crosses.Should().BeEmpty();
        viewModel.Programs[0].Sessions[0].IsDone.Should().BeFalse();
    }

    [TestMethod]
    public async Task Bonus_NoEnrollment_StillLogs()
    {
        var viewModel = CreateViewModel(new FakeCatalogService());
        await viewModel.LoadAsync();
        _bonusDialog.Title = "Ab Attack: 10";

        await viewModel.LogBonusCommand.ExecuteAsync(null);

        viewModel.Bonuses.Should().ContainSingle().Which.Title.Should().Be("Ab Attack: 10");
    }

    [TestMethod]
    public async Task Bonus_Cancelled_DoesNotLog()
    {
        var viewModel = await LoadAsync(_today, "run");
        _bonusDialog.Title = null;

        await viewModel.LogBonusCommand.ExecuteAsync(null);

        viewModel.Bonuses.Should().BeEmpty();
    }

    [TestMethod]
    public async Task RemoveBonus_Confirmed_RemovesIt()
    {
        var viewModel = await LoadAsync(_today, "run");
        _bonusDialog.Title = "Yoga";
        await viewModel.LogBonusCommand.ExecuteAsync(null);

        await viewModel.RemoveBonusCommand.ExecuteAsync(viewModel.Bonuses[0]);

        viewModel.Bonuses.Should().BeEmpty();
    }

    [TestMethod]
    public async Task RemoveBonus_Declined_KeepsIt()
    {
        var viewModel = await LoadAsync(_today, "run");
        _bonusDialog.Title = "Yoga";
        await viewModel.LogBonusCommand.ExecuteAsync(null);
        _confirmation.Result = ConfirmationResult.Denied;

        await viewModel.RemoveBonusCommand.ExecuteAsync(viewModel.Bonuses[0]);

        viewModel.Bonuses.Should().ContainSingle();
    }

    [TestMethod]
    public async Task Load_TwoSessions_EachCrossButtonHasItsOwnName()
    {
        var viewModel = await LoadAsync(_today, "run+abs");

        viewModel.Programs[0].Sessions.Select(s => s.CrossAutomationName).Should().Equal("TodayCrossOffFormat(Run)", "TodayCrossOffFormat(Abs)");
    }

    [TestMethod]
    public async Task Cross_TwoSessions_MarksSitOnTheLatestCrossedCard()
    {
        var viewModel = await LoadAsync(_today, "run+abs");

        await viewModel.Programs[0].Sessions[0].CrossCommand.ExecuteAsync(null);
        viewModel.Programs[0].Sessions.Select(s => s.ShowsMarks).Should().Equal(true, false);

        await viewModel.Programs[0].Sessions[1].CrossCommand.ExecuteAsync(null);
        viewModel.Programs[0].Sessions.Select(s => s.ShowsMarks).Should().Equal(false, true);
    }

    [TestMethod]
    public async Task Bonus_Logged_ListsDetailLine()
    {
        var viewModel = await LoadAsync(_today, "run");
        _bonusDialog.Title = "Yoga";

        await viewModel.LogBonusCommand.ExecuteAsync(null);

        viewModel.BonusItems.Should().ContainSingle().Which.Detail.Should().Be("TodayBonusToday");
    }

    [TestMethod]
    public async Task Load_PastMidnight_TimerMovesToTomorrowsSessions()
    {
        _time.SetUtcNow(new DateTimeOffset(2026, 11, 21, 23, 59, 0, TimeSpan.Zero));
        var viewModel = await LoadAsync(_today, "run", "rest");
        viewModel.Programs[0].DayNumber.Should().Be(1);

        _time.Advance(TimeSpan.FromMinutes(2));

        viewModel.Programs[0].DayNumber.Should().Be(2);
        viewModel.Programs[0].IsRestDay.Should().BeTrue();
    }

    [TestMethod]
    public async Task Load_AcrossNoon_TimerSwitchesGreeting()
    {
        _time.SetUtcNow(new DateTimeOffset(2026, 11, 21, 11, 59, 0, TimeSpan.Zero));
        var viewModel = await LoadAsync(_today, "run");
        viewModel.Greeting.Should().Be("GreetingMorning");

        _time.Advance(TimeSpan.FromMinutes(2));

        viewModel.Greeting.Should().Be("GreetingAfternoon");
    }

    [TestMethod]
    public async Task RefreshIfDateChanged_SleptThroughMidnight_Reloads()
    {
        _time.SetUtcNow(new DateTimeOffset(2026, 11, 21, 23, 0, 0, TimeSpan.Zero));
        var viewModel = await LoadAsync(_today, "run", "rest");
        viewModel.Unload();
        _time.Advance(TimeSpan.FromHours(2));
        viewModel.Programs[0].DayNumber.Should().Be(1);

        await viewModel.RefreshIfDateChangedAsync();

        viewModel.Programs[0].DayNumber.Should().Be(2);
    }

    [TestMethod]
    public async Task RefreshIfDateChanged_SameDayAndGreeting_DoesNotReload()
    {
        var viewModel = await LoadAsync(_today, "run");
        await viewModel.Programs[0].Sessions[0].CrossCommand.ExecuteAsync(null);
        var sessions = viewModel.Programs[0].Sessions;

        await viewModel.RefreshIfDateChangedAsync();

        viewModel.Programs[0].Sessions.Should().BeSameAs(sessions);
    }

    [TestMethod]
    public async Task Today_TwoPrograms_GroupsSessions()
    {
        var viewModel = await LoadTwoAsync();

        viewModel.HasSeveralPrograms.Should().BeTrue();
        viewModel.Programs.Select(p => (p.ProgramName, p.Sessions.Count)).Should().BeEquivalentTo(
            [("Program p r1", 1), ("Program q r1", 2)]);
        viewModel.Programs.SelectMany(p => p.Sessions).Should().OnlyContain(s => s.Program != null);
    }

    [TestMethod]
    public async Task Today_TwoPrograms_SelectedOneComesFirst()
    {
        var viewModel = await LoadTwoAsync();
        var q = viewModel.Programs.Single(p => p.ProgramName == "Program q r1");

        await _service!.SelectAsync(q.EnrollmentId);
        await viewModel.RefreshAsync();

        viewModel.Programs[0].EnrollmentId.Should().Be(q.EnrollmentId);
    }

    [TestMethod]
    public async Task Cross_InSecondProgram_WritesThatEnrollmentOnly()
    {
        var viewModel = await LoadTwoAsync();
        var q = viewModel.Programs.Single(p => p.ProgramName == "Program q r1");

        await q.Sessions[1].CrossCommand.ExecuteAsync(null);

        _repository.CrossesByEnrollment.Should().Equal((q.EnrollmentId, 0, 1));
        q.Sessions[1].IsDone.Should().BeTrue();
        viewModel.Programs.Single(p => p != q).Sessions[0].IsDone.Should().BeFalse();
    }

    [TestMethod]
    public async Task Lapse_ReadOnlyProgram_CannotCrossFromToday()
    {
        var viewModel = await LoadTwoAsync();
        var p = viewModel.Programs.Single(x => x.ProgramName == "Program p r1");
        await _service!.SetPrimaryAsync(p.EnrollmentId);
        _entitlements.SetPro(false);
        await viewModel.RefreshAsync();
        var q = viewModel.Programs.Single(x => x != p);

        await q.Sessions[0].CrossCommand.ExecuteAsync(null);

        q.IsReadOnly.Should().BeTrue();
        q.Sessions.Should().OnlyContain(s => !s.CanCross);
        p.IsReadOnly.Should().BeFalse();
        _repository.Crosses.Should().BeEmpty();
    }

    private async Task<TodayViewModel> LoadTwoAsync()
    {
        FakeCatalogService catalog = new(TestCatalog.Program("p", 1, "run"), TestCatalog.Program("q", 1, "run+abs"));
        var viewModel = CreateViewModel(catalog);
        await _repository.EnrollAsync("p", 1, "t", _today);
        _time.Advance(TimeSpan.FromSeconds(1));
        await _repository.EnrollAsync("q", 1, "t", _today);
        await viewModel.LoadAsync();
        return viewModel;
    }

    private async Task<TodayViewModel> LoadAsync(DateOnly start, params string[] days)
    {
        var program = TestCatalog.Program("p", 1, days);
        var viewModel = CreateViewModel(new FakeCatalogService(program));
        await _repository.EnrollAsync("p", 1, "t", start);
        await viewModel.LoadAsync();
        return viewModel;
    }

    private TodayViewModel CreateViewModel(FakeCatalogService catalog)
    {
        WeakReferenceMessenger messenger = new();
        var service = _service = TestEnrollments.Create(_repository, catalog, messenger, _settings, _entitlements);
        return new(
            new FakeStringLocalizer(),
            service,
            new ScheduleQuery(service, catalog),
            catalog,
            _time,
            messenger,
            new CrossingCoordinator(_preferences, _crossMoment, new FakeMarkingFeedback(), service, new StubJournalService(), _journalDialogs),
            _confirmation,
            _bonusDialog,
            new FakeSectionNavigator(),
            _journalDialogs,
            new RecordingNavigationService(),
            NullLogger<TodayViewModel>.Instance);
    }
}
