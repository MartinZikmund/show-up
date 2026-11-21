using AwesomeAssertions;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Microsoft.UI.Xaml;
using ShowUp.Core.Marking;
using ShowUp.Core.Navigation;
using ShowUp.Core.Services;
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
    private readonly FakeCrossMoment _crossMoment = new();
    private readonly FakeConfirmationDialogService _confirmation = new();
    private readonly FakeCalendarPrompts _prompts = new();
    private readonly FakeSectionNavigator _navigator = new();
    private ActiveEnrollmentService? _service;

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
        await _service!.ShiftAsync(_today.AddDays(-4), 2);
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
    public void BrowsePrograms_EmptyState_NavigatesToPrograms()
    {
        var viewModel = CreateViewModel(new FakeCatalogService());

        viewModel.BrowseProgramsCommand.Execute(null);

        _navigator.Sections.Should().Equal(NavigationSection.Programs);
    }

    private static CalendarDayViewModel Day(ProgramCalendarViewModel viewModel, int index) =>
        viewModel.Weeks.SelectMany(w => w.Days).Single(d => d.DayIndex == index);

    private async Task<ProgramCalendarViewModel> LoadAsync(DateOnly start, params string[] days)
    {
        var program = TestCatalog.Program("p", 1, days);
        var viewModel = CreateViewModel(new FakeCatalogService(program));
        await _repository.EnrollAsync("p", 1, "t", start);
        await viewModel.LoadAsync();
        return viewModel;
    }

    private ProgramCalendarViewModel CreateViewModel(FakeCatalogService catalog)
    {
        WeakReferenceMessenger messenger = new();
        _service = new(_repository, catalog, messenger, NullLogger<ActiveEnrollmentService>.Instance);
        return new(
            new FakeStringLocalizer(),
            _service,
            catalog,
            _time,
            messenger,
            _preferences,
            _crossMoment,
            new FakeMarkingFeedback(),
            _confirmation,
            _prompts,
            _navigator);
    }

    private sealed class FakeStringLocalizer : IStringLocalizer
    {
        public LocalizedString this[string name] => new(name, name, resourceNotFound: true);

        public LocalizedString this[string name, params object[] arguments] => new(name, $"{name}({string.Join(",", arguments)})", resourceNotFound: true);

        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];
    }

    private sealed class FakeCrossMoment : ICrossMoment
    {
        public List<CrossMomentInfo> Shown { get; } = [];

        public CrossMomentResult DrawResult { get; set; } = CrossMomentResult.Auto;

        public Task ShowAsync(CrossMomentInfo info)
        {
            Shown.Add(info);
            return Task.CompletedTask;
        }

        public Task<CrossMomentResult> DrawAsync(CrossMomentDrawRequest request) => Task.FromResult(DrawResult);
    }

    private sealed class FakeMarkingFeedback : IMarkingFeedback
    {
        public bool SoundEnabled { get; set; }

        public bool HapticsEnabled { get; set; }

        public void StrokeDrawn() { }

        public void SessionTicked() { }

        public void CrossLanded() { }

        public void ScratchStart() { }

        public void ScratchSpeed(double normalizedSpeed) { }

        public void ScratchStop() { }

        public void DayTaken() { }
    }

    private sealed class FakeConfirmationDialogService : IConfirmationDialogService
    {
        public ConfirmationResult Result { get; set; } = ConfirmationResult.Confirmed;

        public Task<ConfirmationResult> ShowAsync(string title, string text) => Task.FromResult(Result);
    }

    private sealed class FakeCalendarPrompts : ICalendarPrompts
    {
        public int? ShiftDays { get; set; }

        public string? BonusTitle { get; set; }

        public Task<int?> AskShiftDaysAsync() => Task.FromResult(ShiftDays);

        public Task<string?> AskBonusTitleAsync() => Task.FromResult(BonusTitle);
    }

    private sealed class FakeSectionNavigator : ISectionNavigator
    {
        public List<NavigationSection> Sections { get; } = [];

        public void NavigateToSection(NavigationSection section) => Sections.Add(section);
    }

    private sealed class FakeAppPreferences : IAppPreferences
    {
        public int DataVersion { get; set; }

        public bool FirstStart { get; set; }

        public int LaunchCount { get; set; }

        public bool OfferUserRating { get; set; }

        public ElementTheme Theme { get; set; }

        public bool MarkerSoundEnabled { get; set; }

        public bool MarkerHapticsEnabled { get; set; }

        public CrossMomentMode CrossMomentMode { get; set; }

        public CrossOffStyle CrossOffStyle { get; set; }
    }
}
