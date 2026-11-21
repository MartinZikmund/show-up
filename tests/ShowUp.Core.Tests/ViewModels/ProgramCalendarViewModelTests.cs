using AwesomeAssertions;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ShowUp.Core.Catalog;
using ShowUp.Core.Marking;
using ShowUp.Core.Navigation;
using ShowUp.Core.Services;
using ShowUp.Core.Storage;
using ShowUp.Core.Tests.Tracking;
using ShowUp.Core.Tracking;
using ShowUp.Core.ViewModels;
using ShowUp.Services.Dialogs;
using ShowUp.Services.Settings;

namespace ShowUp.Core.Tests.ViewModels;

[TestClass]
public sealed class ProgramCalendarViewModelTests
{
    private static readonly DateOnly _today = new(2026, 11, 21);

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 21, 10, 0, 0, TimeSpan.Zero));
    private readonly FakeTrackingRepository _repository;
    private readonly FakeAppPreferences _preferences = new() { CrossOffStyle = CrossOffStyle.AutoMarker, CrossMomentMode = CrossMomentMode.Never };
    private readonly RecordingJournalDialogs _journalDialogs = new();
    private readonly RecordingNavigationService _navigation = new();
    private readonly FakeCrossMoment _crossMoment = new();
    private readonly FakeConfirmationDialogService _confirmation = new();
    private readonly FakeCalendarPrompts _prompts = new();
    private readonly FakeBonusWorkoutDialog _bonusDialog = new();
    private readonly FakeDialogService _dialogs = new();
    private readonly FakeSectionNavigator _navigator = new();
    private ActiveEnrollmentService? _service;
    private CountingEnrollmentService? _counting;
    private Guid _enrollmentId;
    private readonly FakeSyncedSettings _settings = new();
    private readonly FakeAppPreferences _selection = new();
    private readonly ShowUp.Core.Tests.Entitlements.FakeEntitlementService _entitlements = new() { IsPro = true };

    public ProgramCalendarViewModelTests()
    {
        _time.SetLocalTimeZone(TimeZoneInfo.Utc);
        _repository = new(_time);
    }

    [TestMethod]
    public async Task CrossNext_FutureDay_DoesNotWrite()
    {
        var viewModel = await LoadAsync(_today, "run", "run", "run");
        var future = Day(viewModel, 2);

        await viewModel.CrossNextCommand.ExecuteAsync(future);

        _repository.Crosses.Should().BeEmpty();
        future.ShakeRequested.Should().BeTrue();
    }

    [TestMethod]
    public async Task CrossNext_TwoSessionDay_FirstTapDrawsOneStroke()
    {
        var viewModel = await LoadAsync(_today, "run+abs");
        var day = Day(viewModel, 0);

        await viewModel.CrossNextCommand.ExecuteAsync(day);

        _repository.Crosses.Should().ContainSingle().Which.Session.Should().Be(0);
        day.MarkStrokes.Should().Be(1);
        day.IsComplete.Should().BeFalse();
    }

    [TestMethod]
    public async Task CrossNext_DoubleTap_CrossesOnce()
    {
        var viewModel = await LoadAsync(_today, "run+abs");
        var day = Day(viewModel, 0);
        _repository.CrossGate = new(TaskCreationOptions.RunContinuationsAsynchronously);

        var first = viewModel.CrossNextCommand.ExecuteAsync(day);
        var second = viewModel.CrossNextCommand.ExecuteAsync(day);
        _repository.CrossGate.SetResult();
        await Task.WhenAll(first, second);

        _repository.Crosses.Should().ContainSingle();
    }

    [TestMethod]
    public async Task CrossNext_AfterShift_MapsToShiftedDay()
    {
        var viewModel = await LoadAsync(_today.AddDays(-5), "run", "run", "run");
        await _service!.ShiftAsync(_enrollmentId, _today.AddDays(-4), 2);
        await viewModel.RefreshAsync();
        var day = Day(viewModel, 1);

        await viewModel.CrossNextCommand.ExecuteAsync(day);

        day.Date.Should().Be(_today.AddDays(-2));
        _repository.Crosses.Should().ContainSingle().Which.Date.Should().Be(_today.AddDays(-2));
    }

    [TestMethod]
    public async Task Load_TodayPastMidnight_RefreshesIsToday()
    {
        _time.SetUtcNow(new DateTimeOffset(2026, 11, 21, 23, 59, 0, TimeSpan.Zero));
        var viewModel = await LoadAsync(_today.AddDays(-1), "run", "run", "run");

        _time.Advance(TimeSpan.FromMinutes(2));

        Day(viewModel, 1).IsToday.Should().BeFalse();
        Day(viewModel, 2).IsToday.Should().BeTrue();
        Day(viewModel, 2).IsFuture.Should().BeFalse();
    }

    [TestMethod]
    public async Task Load_NoEnrollment_HasEnrollmentFalse()
    {
        var viewModel = CreateViewModel(new FakeCatalogService());

        await viewModel.LoadAsync();

        viewModel.HasEnrollment.Should().BeFalse();
        viewModel.ShowEmptyState.Should().BeTrue();
        viewModel.Weeks.Should().BeEmpty();
    }

    [TestMethod]
    public async Task Load_Enrollment_GroupsDaysIntoProgramWeeks()
    {
        var viewModel = await LoadAsync(_today, [.. Enumerable.Repeat("run", 9)]);

        viewModel.Weeks.Select(w => w.Days.Count).Should().Equal(7, 2);
        viewModel.Weeks[0].IsPhaseStart.Should().BeTrue();
        viewModel.Weeks[1].IsPhaseStart.Should().BeFalse();
        viewModel.Weeks[1].Days[0].DayNumber.Should().Be(8);
    }

    [TestMethod]
    public async Task CrossNext_DrawnCross_PersistsInk()
    {
        _preferences.CrossOffStyle = CrossOffStyle.DrawYourself;
        _crossMoment.DrawResult = CrossMomentResult.Drawn("[[0,0,1,1]]");
        var viewModel = await LoadAsync(_today.AddDays(-1), "run", "run");
        var day = Day(viewModel, 0);

        await viewModel.CrossNextCommand.ExecuteAsync(day);

        _repository.Crosses.Should().ContainSingle().Which.Ink.Should().Be("[[0,0,1,1]]");
        day.Inks.Should().Equal("[[0,0,1,1]]");
        day.MarkStrokes.Should().Be(0);
    }

    [TestMethod]
    public async Task CrossNext_DrawThenDoItForMe_MomentSeedMatchesCell()
    {
        _preferences.CrossOffStyle = CrossOffStyle.DrawYourself;
        _crossMoment.DrawResult = CrossMomentResult.Auto;
        var viewModel = await LoadAsync(_today, "run");
        var day = Day(viewModel, 0);

        await viewModel.CrossNextCommand.ExecuteAsync(day);

        _crossMoment.DrawRequests.Should().ContainSingle().Which.Info.Seed.Should().Be(day.Seed);
        day.Seed.Should().NotBe(day.DayIndex);
    }

    [TestMethod]
    public async Task Seed_UncrossAndRecrossFirstSession_KeepsRemainingShape()
    {
        var viewModel = await LoadAsync(_today, "run+abs");
        var day = Day(viewModel, 0);
        await viewModel.CrossNextCommand.ExecuteAsync(day);
        _time.Advance(TimeSpan.FromMinutes(1));
        await viewModel.CrossNextCommand.ExecuteAsync(day);
        _time.Advance(TimeSpan.FromMinutes(1));
        await _service!.UncrossAsync(_enrollmentId, 0, 0);
        await viewModel.RefreshAsync();
        var seedOfRemaining = day.Seed;

        await viewModel.CrossNextCommand.ExecuteAsync(day);

        day.Seed.Should().Be(seedOfRemaining);
        day.MarkStrokes.Should().Be(2);
    }

    [TestMethod]
    public async Task CrossNext_DrawCancelled_DoesNotWrite()
    {
        _preferences.CrossOffStyle = CrossOffStyle.DrawYourself;
        _crossMoment.DrawResult = CrossMomentResult.Cancelled;
        var viewModel = await LoadAsync(_today, "run");

        await viewModel.CrossNextCommand.ExecuteAsync(Day(viewModel, 0));

        _repository.Crosses.Should().BeEmpty();
    }

    [TestMethod]
    public async Task CrossNext_AutoToday_ShowsCrossMomentPerMode()
    {
        _preferences.CrossMomentMode = CrossMomentMode.OnlyToday;
        var viewModel = await LoadAsync(_today.AddDays(-1), "run", "run");

        await viewModel.CrossNextCommand.ExecuteAsync(Day(viewModel, 0));
        await viewModel.CrossNextCommand.ExecuteAsync(Day(viewModel, 1));

        _crossMoment.Shown.Should().ContainSingle().Which.DayNumber.Should().Be(2);
        viewModel.CurrentStreak.Should().Be(2);
    }

    [TestMethod]
    public async Task UncrossLast_Confirmed_UncrossesHighestSession()
    {
        var viewModel = await LoadAsync(_today, "run+abs");
        var day = Day(viewModel, 0);
        await viewModel.CrossNextCommand.ExecuteAsync(day);
        await viewModel.CrossNextCommand.ExecuteAsync(day);

        await viewModel.UncrossLastCommand.ExecuteAsync(day);

        day.MarkStrokes.Should().Be(1);
        day.Sessions.Select(s => s.IsDone).Should().Equal(true, false);
    }

    [TestMethod]
    public async Task UncrossLast_Denied_KeepsCross()
    {
        _confirmation.Result = ConfirmationResult.Denied;
        var viewModel = await LoadAsync(_today, "run");
        var day = Day(viewModel, 0);
        await viewModel.CrossNextCommand.ExecuteAsync(day);

        await viewModel.UncrossLastCommand.ExecuteAsync(day);

        day.IsComplete.Should().BeTrue();
    }

    [TestMethod]
    public async Task ShiftSchedule_DaysChosen_ShiftsFromToday()
    {
        _prompts.ShiftDays = 3;
        var viewModel = await LoadAsync(_today.AddDays(-1), "run", "run", "run");

        await viewModel.ShiftScheduleCommand.ExecuteAsync(null);

        _repository.ShiftCalls.Should().Equal((_today, 3, 3));
        Day(viewModel, 1).Date.Should().Be(_today.AddDays(3));
    }

    [TestMethod]
    public async Task ShiftSchedule_NothingLeftToShift_ShowsMessage()
    {
        _prompts.ShiftDays = 2;
        var viewModel = await LoadAsync(_today, "run");
        await viewModel.CrossNextCommand.ExecuteAsync(Day(viewModel, 0));
        _repository.FailShifts = true;

        await viewModel.ShiftScheduleCommand.ExecuteAsync(null);

        _dialogs.Shown.Should().Equal("ShiftFailedTitle");
    }

    [TestMethod]
    public void BrowsePrograms_EmptyState_NavigatesToPrograms()
    {
        var viewModel = CreateViewModel(new FakeCatalogService());

        viewModel.BrowseProgramsCommand.Execute(null);

        _navigator.Sections.Should().Equal(NavigationSection.Programs);
    }

    [TestMethod]
    public async Task CrossNext_CompleteDayWithOptionalLeft_CrossesOptional()
    {
        var viewModel = await LoadAsync(_today, "run+abs?");
        var day = Day(viewModel, 0);
        await viewModel.CrossNextCommand.ExecuteAsync(day);
        day.IsComplete.Should().BeTrue();
        day.CanCross.Should().BeTrue();

        await viewModel.CrossNextCommand.ExecuteAsync(day);

        _repository.Crosses.Select(c => c.Session).Should().Equal(0, 1);
        day.CanCross.Should().BeFalse();
        day.MarkStrokes.Should().Be(2);
    }

    [TestMethod]
    public async Task Message_ExternalCross_Refreshes()
    {
        var viewModel = await LoadAsync(_today, "run");

        await _service!.CrossAsync(_enrollmentId, 0, 0);

        Day(viewModel, 0).IsComplete.Should().BeTrue();
        viewModel.CurrentStreak.Should().Be(1);
    }

    [TestMethod]
    public async Task CrossNext_OwnWrite_RefreshesOnce()
    {
        var viewModel = await LoadAsync(_today, "run");
        _counting!.Gets = 0;

        await viewModel.CrossNextCommand.ExecuteAsync(Day(viewModel, 0));

        _counting.Gets.Should().Be(1);
    }

    [TestMethod]
    public async Task RefreshIfDateChanged_NextDay_MovesToday()
    {
        var viewModel = await LoadAsync(_today, "run", "run");
        viewModel.Unload();
        _time.SetUtcNow(_time.GetUtcNow().AddDays(1));

        await viewModel.RefreshIfDateChangedAsync();

        Day(viewModel, 1).IsToday.Should().BeTrue();
    }

    [TestMethod]
    public async Task RefreshIfDateChanged_SameDay_DoesNotReload()
    {
        var viewModel = await LoadAsync(_today, "run");
        _counting!.Gets = 0;

        await viewModel.RefreshIfDateChangedAsync();

        _counting.Gets.Should().Be(0);
    }

    [TestMethod]
    public async Task LogBonus_TitleGiven_ShowsBonusOnDay()
    {
        _bonusDialog.Title = "  Evening run ";
        var viewModel = await LoadAsync(_today, "run");
        var day = Day(viewModel, 0);

        await viewModel.LogBonusCommand.ExecuteAsync(day);

        day.BonusCount.Should().Be(1);
        (await _repository.GetBonusWorkoutsAsync(_today, _today)).Should().ContainSingle().Which.Title.Should().Be("Evening run");
    }

    [TestMethod]
    public async Task LogBonus_OneBonus_IsAnnouncedWithThePluralForm()
    {
        _bonusDialog.Title = "Evening run";
        var viewModel = await LoadAsync(_today, "run");
        var day = Day(viewModel, 0);

        await viewModel.LogBonusCommand.ExecuteAsync(day);

        day.AutomationName.Should().Contain("CalendarDayBonus_One(1)");
    }

    [TestMethod]
    public async Task LogBonus_WorkoutWithMetrics_OffersItsResults()
    {
        _bonusDialog.Title = "Fit Test";
        _bonusDialog.WorkoutId = "fit";
        var viewModel = await LoadAsync(_today, "run");

        await viewModel.LogBonusCommand.ExecuteAsync(Day(viewModel, 0));

        _journalDialogs.Calls.Should().Equal("bonus:fit");
    }

    [TestMethod]
    public async Task LogBonus_WorkoutWithoutMetrics_OffersNoResults()
    {
        _bonusDialog.Title = "Run";
        _bonusDialog.WorkoutId = "run";
        var viewModel = await LoadAsync(_today, "run");

        await viewModel.LogBonusCommand.ExecuteAsync(Day(viewModel, 0));

        _journalDialogs.Calls.Should().BeEmpty();
    }

    [TestMethod]
    public async Task OpenDay_CrossedDay_ShowsTheDayDetail()
    {
        var viewModel = await LoadAsync(_today, "run");
        var day = Day(viewModel, 0);
        await viewModel.CrossNextCommand.ExecuteAsync(day);

        await viewModel.OpenDayCommand.ExecuteAsync(day);

        _journalDialogs.Calls.Should().Equal("day:0");
    }

    [TestMethod]
    public async Task OpenResults_WithEnrollment_NavigatesToTheResults()
    {
        var viewModel = await LoadAsync(_today, "fit");

        viewModel.OpenResultsCommand.Execute(null);

        _navigation.Navigations.Should().ContainSingle().Which.ViewModel.Should().Be<ProgramResultsViewModel>();
    }

    [TestMethod]
    public async Task LogBonus_FutureDay_DoesNotWrite()
    {
        _bonusDialog.Title = "Evening run";
        var viewModel = await LoadAsync(_today, "run", "run");

        await viewModel.LogBonusCommand.ExecuteAsync(Day(viewModel, 1));

        (await _repository.GetBonusWorkoutsAsync(_today, _today.AddDays(1))).Should().BeEmpty();
    }

    [TestMethod]
    public async Task UncrossLast_WriteFails_RefreshesWithoutThrowing()
    {
        var viewModel = await LoadAsync(_today, "run");
        var day = Day(viewModel, 0);
        await viewModel.CrossNextCommand.ExecuteAsync(day);
        _counting!.FailWrites = true;

        var act = () => viewModel.UncrossLastCommand.ExecuteAsync(day);

        await act.Should().NotThrowAsync();
        day.IsComplete.Should().BeTrue();
    }

    [TestMethod]
    public async Task Load_OneProgram_HidesSwitcher()
    {
        var viewModel = await LoadAsync(_today, "run");

        viewModel.ShowProgramSwitcher.Should().BeFalse();
        viewModel.SelectedProgram!.Id.Should().Be(_enrollmentId);
    }

    [TestMethod]
    public async Task SelectProgram_Other_ShowsItAndPersists()
    {
        var (viewModel, p, q) = await LoadTwoAsync();
        viewModel.ShowProgramSwitcher.Should().BeTrue();
        viewModel.ShowProgramTitle.Should().BeFalse("the switcher is the title");
        viewModel.ProgramName.Should().Be("Program q r1", "the newest one shows first");

        await viewModel.SelectProgramAsync(viewModel.Programs.Single(o => o.Id == p));

        viewModel.ProgramName.Should().Be("Program p r1");
        viewModel.Weeks.SelectMany(w => w.Days).Should().HaveCount(3);
        _selection.SelectedEnrollmentId.Should().Be(p);
    }

    [TestMethod]
    public async Task CrossNext_SelectedProgram_WritesThatEnrollment()
    {
        var (viewModel, p, _) = await LoadTwoAsync();
        await viewModel.SelectProgramAsync(viewModel.Programs.Single(o => o.Id == p));

        await viewModel.CrossNextCommand.ExecuteAsync(Day(viewModel, 0));

        _repository.CrossesByEnrollment.Should().Equal((p, 0, 0));
    }

    [TestMethod]
    public async Task Lapse_ReadOnlyProgram_CrossShakesWithoutWriting()
    {
        var (viewModel, p, q) = await LoadTwoAsync();
        await _service!.SetPrimaryAsync(p);
        _entitlements.SetPro(false);
        await viewModel.RefreshAsync();
        var day = Day(viewModel, 0);

        await viewModel.CrossNextCommand.ExecuteAsync(day);

        viewModel.SelectedProgram!.Id.Should().Be(q);
        viewModel.IsReadOnly.Should().BeTrue();
        viewModel.IsEditable.Should().BeFalse();
        day.ShakeRequested.Should().BeTrue();
        _repository.Crosses.Should().BeEmpty();
    }

    private async Task<(ProgramCalendarViewModel ViewModel, Guid P, Guid Q)> LoadTwoAsync()
    {
        var viewModel = CreateViewModel(new FakeCatalogService(TestCatalog.Program("p", 1, "run", "run", "run"), TestCatalog.Program("q", 1, "abs")));
        var p = (await _repository.EnrollAsync("p", 1, "t", _today)).Id;
        _time.Advance(TimeSpan.FromSeconds(1));
        var q = (await _repository.EnrollAsync("q", 1, "t", _today)).Id;
        await viewModel.LoadAsync();
        return (viewModel, p, q);
    }

    private static CalendarDayViewModel Day(ProgramCalendarViewModel viewModel, int index) =>
        viewModel.Weeks.SelectMany(w => w.Days).Single(d => d.DayIndex == index);

    private async Task<ProgramCalendarViewModel> LoadAsync(DateOnly start, params string[] days)
    {
        var program = TestCatalog.Program("p", 1, days);
        var viewModel = CreateViewModel(new FakeCatalogService(program));
        _enrollmentId = (await _repository.EnrollAsync("p", 1, "t", start)).Id;
        await viewModel.LoadAsync();
        return viewModel;
    }

    private ProgramCalendarViewModel CreateViewModel(FakeCatalogService catalog)
    {
        WeakReferenceMessenger messenger = new();
        _service = TestEnrollments.Create(_repository, catalog, messenger, _settings, _entitlements, _selection);
        _counting = new(_service);
        return new(
            new FakeStringLocalizer(),
            _counting,
            catalog,
            _time,
            messenger,
            new CrossingCoordinator(_preferences, _crossMoment, new FakeMarkingFeedback(), _counting, new StubJournalService(), _journalDialogs),
            _confirmation,
            _dialogs,
            _prompts,
            _bonusDialog,
            _navigator,
            _journalDialogs,
            _navigation,
            NullLogger<ProgramCalendarViewModel>.Instance);
    }

    /// <summary>Counts loads, and can fail writes the way an enrollment that ended under the page does.</summary>
    private sealed class CountingEnrollmentService(IActiveEnrollmentService inner) : IActiveEnrollmentService
    {
        public int Gets { get; set; }

        public bool FailWrites { get; set; }

        public Guid? SelectedEnrollmentId => inner.SelectedEnrollmentId;

        public Task<IReadOnlyList<ActiveEnrollment>> GetAllAsync() => inner.GetAllAsync();

        public Task<IReadOnlyList<EnrollmentRecord>> GetHistoryAsync() => inner.GetHistoryAsync();

        public Task<ActiveEnrollment?> GetSelectedAsync()
        {
            Gets++;
            return inner.GetSelectedAsync();
        }

        public Task<ActiveEnrollment?> GetAsync(Guid enrollmentId) => inner.GetAsync(enrollmentId);

        public Task SelectAsync(Guid enrollmentId) => inner.SelectAsync(enrollmentId);

        public Task<ActiveEnrollment?> GetPrimaryAsync() => inner.GetPrimaryAsync();

        public Task<bool> HasPrimaryChoiceAsync() => inner.HasPrimaryChoiceAsync();

        public Task SetPrimaryAsync(Guid enrollmentId) => inner.SetPrimaryAsync(enrollmentId);

        public bool IsReadOnly(ActiveEnrollment enrollment) => inner.IsReadOnly(enrollment);

        public Task<IReadOnlyList<CompletedSession>> GetCompletionsAsync(Guid enrollmentId) => inner.GetCompletionsAsync(enrollmentId);

        public Task<IReadOnlyList<BonusWorkoutEntity>> GetBonusWorkoutsAsync(Guid enrollmentId) => inner.GetBonusWorkoutsAsync(enrollmentId);

        public Task<ActiveEnrollment> EnrollAsync(WorkoutProgram program, ProgramTrack track, DateOnly startDate) =>
            inner.EnrollAsync(program, track, startDate);

        public Task<ActiveEnrollment> ReplaceAsync(Guid enrollmentId, EnrollmentEndReason reason, WorkoutProgram program, ProgramTrack track, DateOnly startDate) =>
            inner.ReplaceAsync(enrollmentId, reason, program, track, startDate);

        public Task EndAsync(Guid enrollmentId, EnrollmentEndReason reason) => Write(() => inner.EndAsync(enrollmentId, reason));

        public Task CrossAsync(Guid enrollmentId, int dayIndex, int sessionIndex, string? inkJson = null, Guid? completionId = null) =>
            Write(() => inner.CrossAsync(enrollmentId, dayIndex, sessionIndex, inkJson, completionId));

        public Task UncrossAsync(Guid enrollmentId, int dayIndex, int sessionIndex) => Write(() => inner.UncrossAsync(enrollmentId, dayIndex, sessionIndex));

        public Task ShiftAsync(Guid enrollmentId, DateOnly fromDate, int days) => Write(() => inner.ShiftAsync(enrollmentId, fromDate, days));

        public Task<IReadOnlyList<BonusWorkoutEntity>> GetBonusWorkoutsAsync(DateOnly from, DateOnly to) => inner.GetBonusWorkoutsAsync(from, to);

        public async Task<BonusWorkoutEntity> LogBonusAsync(string? workoutId, string title, DateOnly date, int? durationMinutes)
        {
            await Write(() => Task.CompletedTask);
            return await inner.LogBonusAsync(workoutId, title, date, durationMinutes);
        }

        public Task RemoveBonusAsync(Guid id) => inner.RemoveBonusAsync(id);

        private Task Write(Func<Task> write) =>
            FailWrites ? Task.FromException(new InvalidOperationException("Enrollment ended.")) : write();
    }

    private sealed class FakeDialogService : IDialogService
    {
        public List<string> Shown { get; } = [];

        public Task<ContentDialogResult> ShowAsync(string title, string content)
        {
            Shown.Add(title);
            return Task.FromResult(ContentDialogResult.Primary);
        }

        public Task<ContentDialogResult> ShowAsync(ContentDialog contentDialog) => Task.FromResult(ContentDialogResult.Primary);
    }

    private sealed class FakeCalendarPrompts : ICalendarPrompts
    {
        public int? ShiftDays { get; set; }

        public Task<int?> AskShiftDaysAsync() => Task.FromResult(ShiftDays);
    }
}
