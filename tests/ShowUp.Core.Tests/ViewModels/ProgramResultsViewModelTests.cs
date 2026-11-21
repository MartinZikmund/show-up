using AwesomeAssertions;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using ShowUp.Core.Catalog;
using ShowUp.Core.Messages;
using ShowUp.Core.Storage;
using ShowUp.Core.Tests.Tracking;
using ShowUp.Core.Tracking;
using ShowUp.Core.ViewModels;

namespace ShowUp.Core.Tests.ViewModels;

[TestClass]
public sealed class ProgramResultsViewModelTests
{
    private static readonly DateOnly _start = new(2026, 11, 1);
    private static readonly Guid _enrollment = Guid.NewGuid();

    private static readonly Workout _fit = new("fit", "Fit Test", 30, WorkoutKind.FitTest, null, [new("row", "Row", MetricUnit.Weight)]);
    private static readonly Workout _sprint = new("sprint", "Sprint", 10, WorkoutKind.Cardio, null, [new("time", "Max Out Time", MetricUnit.Duration)]);

    private readonly StubJournalService _journal = new();
    private readonly FakeAppPreferences _preferences = new() { UnitSystem = UnitSystem.Imperial };

    [TestMethod]
    public async Task Load_WorkoutsWithoutValues_CollapseToTheirNextDay()
    {
        _journal.Results = Build([Entry(0, "fit", "row", 10)]);
        var viewModel = Create(today: _start.AddDays(2));

        await viewModel.LoadAsync();

        viewModel.Tables.Select(t => t.Title).Should().Equal("Fit Test");
        viewModel.Pending.Should().ContainSingle().Which.Should().BeEquivalentTo(new { Title = "Sprint", Detail = "ResultsNotLoggedNextFormat(4)" });
        viewModel.ShowEmptyState.Should().BeFalse();
    }

    [TestMethod]
    public async Task Load_TableColumns_StopAtTheNextOccurrence()
    {
        _journal.Results = Build([Entry(0, "fit", "row", 10)]);
        var viewModel = Create(today: _start);

        await viewModel.LoadAsync();

        viewModel.Tables.Single().Columns.Select(c => c.Label).Should().Equal("CalendarDayNumberFormat", "CalendarDayNumberFormat");
    }

    [TestMethod]
    public async Task Load_Cell_SpeaksMetricColumnValueUnitAndChange()
    {
        _journal.Results = Build([Entry(0, "fit", "row", 10), Entry(2, "fit", "row", 12)]);
        var viewModel = Create(today: _start.AddDays(2));

        await viewModel.LoadAsync();

        var row = viewModel.Tables.Single().Rows.Single();
        row.UnitHint.Should().Be("MetricUnitLb");
        row.Cells[1].AutomationName.Should().Be("ResultsCellDeltaFormat(Row,CalendarDayNumberFormat,26.46 MetricUnitLb,+4.41)");
        row.Cells[1].IsBetter.Should().BeTrue();
    }

    [TestMethod]
    public async Task ViewLoaded_CalledTwice_ListensOnce()
    {
        var viewModel = Create(today: _start);

        viewModel.ViewLoaded();
        viewModel.ViewLoaded();
        await Task.Delay(50);
        var calls = _journal.ResultsCalls;
        _messenger.Send(new JournalChangedMessage());
        await Task.Delay(50);

        _journal.ResultsCalls.Should().Be(calls + 1);
    }

    [TestMethod]
    public async Task Load_Overlapping_RunsOneAtATime()
    {
        _journal.ResultsGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var viewModel = Create(today: _start);

        var first = viewModel.LoadAsync();
        var second = viewModel.LoadAsync();
        await Task.Delay(50);
        _journal.ResultsGate.SetResult();
        await Task.WhenAll(first, second);

        _journal.ResultsCalls.Should().Be(2);
        _journal.MaxConcurrentLoads.Should().Be(1);
    }

    private readonly WeakReferenceMessenger _messenger = new();

    private ProgramResultsViewModel Create(DateOnly today)
    {
        FakeTimeProvider time = new(new DateTimeOffset(today.ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero));
        time.SetLocalTimeZone(TimeZoneInfo.Utc);
        return new(
            _journal,
            new RecordingEnrollmentService(),
            new RecordingJournalDialogs(),
            new RecordingNavigationService(),
            _messenger,
            new FakeStringLocalizer(),
            _preferences,
            time,
            NullLogger<ProgramResultsViewModel>.Instance);
    }

    // Days: 0 fit, 1 sprint, 2 fit, 3 sprint, 4 fit.
    private static IReadOnlyList<ResultsTable> Build(IEnumerable<MetricEntryEntity> entries) =>
        ResultsTable.Build(
            [Day("fit"), Day("sprint"), Day("fit"), Day("sprint"), Day("fit")],
            i => _start.AddDays(i),
            new Dictionary<string, Workout> { ["fit"] = _fit, ["sprint"] = _sprint },
            entries,
            [],
            []);

    private static ScheduleDay Day(string workoutId) => new([new ScheduledSession(workoutId)]);

    private static MetricEntryEntity Entry(int day, string workout, string metric, double value) => new()
    {
        EnrollmentId = _enrollment,
        DayIndex = day,
        SessionIndex = 0,
        SubjectKey = SubjectKeys.Session(_enrollment, day, 0),
        WorkoutId = workout,
        MetricId = metric,
        Value = value,
    };
}
