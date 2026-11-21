using System.Globalization;
using AwesomeAssertions;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using ShowUp.Core.Catalog;
using ShowUp.Core.Messages;
using ShowUp.Core.Navigation;
using ShowUp.Core.Storage;
using ShowUp.Core.Tests.Entitlements;
using ShowUp.Core.Tests.Tracking;
using ShowUp.Core.Tracking;
using ShowUp.Core.ViewModels;

namespace ShowUp.Core.Tests.ViewModels;

[TestClass]
public sealed class HistoryViewModelTests
{
    private static readonly DateOnly _today = new(2026, 11, 21);

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 21, 10, 0, 0, TimeSpan.Zero));
    private readonly FakeTrackingRepository _repository;
    private readonly FakeEntitlementService _entitlements = new() { IsPro = true };
    private readonly FakeCatalogService _catalog = new(
        TestCatalog.Program("old", 1, "run", "run", "abs", "fit", "run", "rest", "rest"),
        TestCatalog.Program("new", 1, "run", "run", "abs", "run", "run", "rest", "rest"));
    private readonly FakeSyncedSettings _settings = new();
    private readonly WeakReferenceMessenger _messenger = new();
    private readonly RecordingNavigationService _navigation = new();
    private readonly RecordingSectionNavigator _navigator = new();
    private ActiveEnrollmentService _service = null!;

    public HistoryViewModelTests()
    {
        CultureInfo.CurrentCulture = new CultureInfo("en-US");
        _time.SetLocalTimeZone(TimeZoneInfo.Utc);
        _repository = new(_time);
    }

    [TestMethod]
    public async Task Free_ShowsOnlyCurrentProgram()
    {
        _entitlements.IsPro = false;
        var first = await _repository.EnrollAsync("old", 1, "t", _today.AddDays(-20));
        await _repository.ReplaceEnrollmentAsync(first.Id, EnrollmentEndReason.Abandoned, "new", 1, "t", _today.AddDays(-3));
        var viewModel = Create();

        await viewModel.LoadAsync();

        viewModel.Programs.Select(p => p.Name).Should().Equal("Program new r1");
        viewModel.ShowGate.Should().BeTrue();
    }

    [TestMethod]
    public async Task Free_ShowsOnlyThirtyDaysOfBonuses_AndGatesTheRest()
    {
        _entitlements.IsPro = false;
        await _repository.LogBonusAsync(null, "Recent", _today.AddDays(-29), 20);
        await _repository.LogBonusAsync(null, "Old", _today.AddDays(-30), null);
        var viewModel = Create();

        await viewModel.LoadAsync();

        viewModel.BonusMonths.SelectMany(m => m.Items).Select(b => b.Title).Should().Equal("Recent");
        viewModel.ShowGate.Should().BeTrue();
    }

    [TestMethod]
    public async Task Free_WithNothingHidden_ShowsNoGate()
    {
        _entitlements.IsPro = false;
        await _repository.EnrollAsync("new", 1, "t", _today);
        await _repository.LogBonusAsync(null, "Recent", _today, 20);
        var viewModel = Create();

        await viewModel.LoadAsync();

        viewModel.ShowGate.Should().BeFalse();
    }

    [TestMethod]
    public async Task Pro_ShowsEndedPrograms()
    {
        var first = await _repository.EnrollAsync("old", 1, "t", _today.AddDays(-20));
        var second = await _repository.ReplaceEnrollmentAsync(first.Id, EnrollmentEndReason.Abandoned, "new", 1, "t", _today.AddDays(-3));
        _time.Advance(TimeSpan.FromHours(1));
        await _repository.ReplaceEnrollmentAsync(second.Id, EnrollmentEndReason.Finished, "old", 1, "t", _today);
        var viewModel = Create();

        await viewModel.LoadAsync();

        viewModel.Programs.Select(p => (p.Name, p.Status)).Should().Equal(
            ("Program old r1", HistoryStatus.Active),
            ("Program new r1", HistoryStatus.Finished),
            ("Program old r1", HistoryStatus.Stopped));
        viewModel.ShowGate.Should().BeFalse();
    }

    [TestMethod]
    public async Task Program_ShowsDaysCrossedAndLongestStreak()
    {
        var enrollment = await _repository.EnrollAsync("new", 1, "t", _today.AddDays(-4));
        foreach (var day in new[] { 0, 1, 2, 4 })
        {
            await _repository.CrossSessionAsync(enrollment.Id, day, 0, _today.AddDays(day - 4));
        }

        var viewModel = Create();

        await viewModel.LoadAsync();

        var program = viewModel.Programs.Should().ContainSingle().Subject;
        program.ProgressText.Should().Be("TodayDayFormat(5,7)");
        program.CrossedText.Should().Be("HistoryCrossedFormat(4)");
        program.BestText.Should().Be("CalendarLongestStreakFormat(3)");
        program.DatesText.Should().Be("HistoryStartedFormat(11/17/2026)");
    }

    [TestMethod]
    public async Task EndedProgram_ShowsDateRange_AndNoLinks()
    {
        var first = await _repository.EnrollAsync("old", 1, "t", _today.AddDays(-20));
        await _repository.ReplaceEnrollmentAsync(first.Id, EnrollmentEndReason.Abandoned, "new", 1, "t", _today);
        var viewModel = Create();

        await viewModel.LoadAsync();

        var ended = viewModel.Programs.Single(p => !p.IsActive);
        ended.DatesText.Should().Be("HistoryRangeFormat(11/1/2026,11/21/2026)");
        ended.ShowResultsLink.Should().BeFalse();
        await viewModel.OpenCalendarCommand.ExecuteAsync(ended);
        _navigator.Sections.Should().BeEmpty();
    }

    [TestMethod]
    public async Task ActiveProgramWithMetrics_OpensResultsForThatEnrollment()
    {
        var enrollment = await _repository.EnrollAsync("old", 1, "t", _today);
        var viewModel = Create();
        await viewModel.LoadAsync();

        var program = viewModel.Programs.Single();
        viewModel.OpenResultsCommand.Execute(program);

        program.ShowResultsLink.Should().BeTrue();
        _navigation.Navigations.Should().Equal((typeof(ProgramResultsViewModel), (object?)enrollment.Id));
    }

    [TestMethod]
    public async Task ProgramWithoutMetrics_HasNoResultsLink()
    {
        await _repository.EnrollAsync("new", 1, "t", _today);
        var viewModel = Create();

        await viewModel.LoadAsync();

        viewModel.Programs.Single().ShowResultsLink.Should().BeFalse();
    }

    [TestMethod]
    public async Task ActiveProgram_OpensCalendar()
    {
        var enrollment = await _repository.EnrollAsync("new", 1, "t", _today);
        var viewModel = Create();
        await viewModel.LoadAsync();

        await viewModel.OpenCalendarCommand.ExecuteAsync(viewModel.Programs.Single());

        _service.SelectedEnrollmentId.Should().Be(enrollment.Id);
        _navigator.Sections.Should().Equal(NavigationSection.Calendar);
    }

    [TestMethod]
    public async Task Bonus_GroupsByMonth()
    {
        await _repository.LogBonusAsync(null, "Oct run", new DateOnly(2026, 10, 30), 30);
        await _repository.LogBonusAsync(null, "Early Nov", new DateOnly(2026, 11, 2), null);
        await _repository.LogBonusAsync(null, "Late Nov", new DateOnly(2026, 11, 20), 45);
        var viewModel = Create();

        await viewModel.LoadAsync();

        viewModel.BonusMonths.Select(m => m.Header).Should().Equal("November 2026", "October 2026");
        viewModel.BonusMonths[0].Items.Select(b => b.Title).Should().Equal("Late Nov", "Early Nov");
        viewModel.BonusMonths[0].Items[0].DurationText.Should().Be("TodayDurationFormat(45)");
        viewModel.BonusMonths[0].Items[1].DurationText.Should().BeEmpty();
    }

    [TestMethod]
    public async Task Load_NothingYet_ShowsEmptyState()
    {
        var viewModel = Create();

        await viewModel.LoadAsync();

        viewModel.ShowEmptyState.Should().BeTrue();
    }

    [TestMethod]
    public async Task TrackingChanged_Reloads()
    {
        var viewModel = Create();
        viewModel.ViewLoaded();
        await viewModel.LoadAsync();
        await _repository.LogBonusAsync(null, "Late", _today, 10);
        TaskCompletionSource reloaded = new();
        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(HistoryViewModel.HasBonuses))
            {
                reloaded.TrySetResult();
            }
        };

        _messenger.Send(new TrackingChangedMessage());

        await reloaded.Task.WaitAsync(TimeSpan.FromSeconds(5));
        viewModel.HasBonuses.Should().BeTrue();
        viewModel.ViewUnloaded();
    }

    private HistoryViewModel Create()
    {
        _service = TestEnrollments.Create(_repository, _catalog, _messenger, _settings, _entitlements);
        return new(
            _service,
            _catalog,
            _entitlements,
            _navigation,
            _navigator,
            _messenger,
            new FakeStringLocalizer(),
            _time,
            NullLogger<HistoryViewModel>.Instance);
    }
}
