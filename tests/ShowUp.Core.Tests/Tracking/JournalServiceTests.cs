using AwesomeAssertions;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging.Abstractions;
using ShowUp.Core.Messages;
using ShowUp.Core.Tests.Storage;
using ShowUp.Core.Tests.ViewModels;
using ShowUp.Core.Tracking;
using ShowUp.Core.ViewModels;

namespace ShowUp.Core.Tests.Tracking;

[TestClass]
public sealed class JournalServiceTests : StorageTestBase
{
    private static readonly DateOnly _start = new(2026, 12, 1);

    private readonly WeakReferenceMessenger _messenger = new();
    private readonly FakeAppPreferences _preferences = new();
    private ActiveEnrollmentService _enrollments = null!;
    private FakeTrackingRepository _repository = null!;
    private JournalService _journal = null!;
    private int _changes;

    [TestInitialize]
    public async Task InitAsync()
    {
        var program = TestCatalog.Program("p", 1, "fit", "run+fit", "run", "fit");
        FakeCatalogService catalog = new(program);
        _repository = new(Time);
        _enrollments = new(_repository, catalog, _messenger, NullLogger<ActiveEnrollmentService>.Instance);
        _journal = new(_enrollments, catalog, CreateNotesRepository(), CreateMetricsRepository(), _messenger);
        _messenger.Register<JournalChangedMessage>(this, (_, _) => _changes++);
        await _enrollments.EnrollAsync(program, program.Tracks[0], _start);
    }

    [TestMethod]
    public async Task GetDay_SessionWithMetrics_HasASheetOthersDoNot()
    {
        await _enrollments.CrossAsync(1, 0);

        var day = await _journal.GetDayAsync(1);

        day!.Date.Should().Be(_start.AddDays(1));
        day.Sessions.Select(s => (s.Name, s.IsDone, s.Results is null)).Should().Equal(("Run", true, true), ("Fit Test", false, false));
    }

    [TestMethod]
    public async Task Notes_ProgramDayAndSession_RoundTripThroughTheJournal()
    {
        await _journal.SaveProgramNoteAsync("Round two");
        await _journal.SaveDayAsync(1, new DayEdit("Hot day", new Dictionary<int, string> { [1] = "New record" }, []));

        (await _journal.GetProgramNoteAsync()).Should().Be("Round two");
        var day = await _journal.GetDayAsync(1);
        day!.Note.Should().Be("Hot day");
        day.Sessions[1].Note.Should().Be("New record");
        day.Sessions[0].Note.Should().BeEmpty();
        _changes.Should().Be(2);
    }

    [TestMethod]
    public async Task SaveResults_TwoFitTests_TableShowsTheDelta()
    {
        var first = (await _journal.GetResultsSheetAsync(0, 0))!;
        var second = (await _journal.GetResultsSheetAsync(3, 0))!;

        await _journal.SaveResultsAsync(first.Target, new Dictionary<string, MetricValue> { ["jacks"] = new(40), ["time"] = new(95) });
        await _journal.SaveResultsAsync(second.Target, new Dictionary<string, MetricValue> { ["jacks"] = new(48) });

        var table = (await _journal.GetResultsAsync()).Should().ContainSingle().Subject;
        table.Columns.Select(c => c.DayIndex).Should().Equal(0, 1, 3);
        table.Rows[0].Cells.Select(c => c.Delta).Should().Equal(null, null, 8);
        (await _journal.GetResultsSheetAsync(0, 0))!.Values["time"].Number.Should().Be(95);
    }

    [TestMethod]
    public async Task BonusResults_FitTestLoggedAsBonus_JoinsTheTable()
    {
        var bonus = await _enrollments.LogBonusAsync("fit", "Fit Test", _start.AddDays(2), null);
        var sheet = (await _journal.GetBonusResultsSheetAsync(bonus))!;

        await _journal.SaveResultsAsync(sheet.Target, new Dictionary<string, MetricValue> { ["jacks"] = new(41) });

        var table = (await _journal.GetResultsAsync()).Single();
        table.Columns.Should().Contain(c => c.BonusWorkoutId == bonus.Id && c.Date == _start.AddDays(2));
        (await _journal.GetDayAsync(2))!.Bonuses.Should().ContainSingle().Which.Results!.Values["jacks"].Number.Should().Be(41);
    }

    [TestMethod]
    public async Task GetResultsSheet_WorkoutWithoutMetrics_IsNull() =>
        (await _journal.GetResultsSheetAsync(2, 0)).Should().BeNull();

    [TestMethod]
    public async Task DayDetail_Save_WritesOnlyWhatChanged()
    {
        DayDetailViewModel viewModel = new(_journal, _enrollments, new FakeStringLocalizer(), _preferences, NullLogger<DayDetailViewModel>.Instance);
        await viewModel.LoadAsync(1);
        viewModel.Sessions[1].Note = "Felt strong";
        viewModel.Sessions[1].Inputs[0].Number = 52;
        viewModel.Sessions[1].Inputs[1].Text = "1:45";

        await viewModel.SaveAsync();

        var day = await _journal.GetDayAsync(1);
        day!.Sessions[1].Note.Should().Be("Felt strong");
        day.Sessions[1].Results!.Values.Should().BeEquivalentTo(new Dictionary<string, MetricValue> { ["jacks"] = new(52), ["time"] = new(105) });
        _changes.Should().Be(1, "the day sheet saves as one change");
    }

    [TestMethod]
    public async Task DayDetail_InvalidDuration_CannotSave()
    {
        DayDetailViewModel viewModel = new(_journal, _enrollments, new FakeStringLocalizer(), _preferences, NullLogger<DayDetailViewModel>.Instance);
        await viewModel.LoadAsync(0);

        viewModel.Sessions[0].Inputs[1].Text = "90";

        viewModel.CanSave.Should().BeFalse();
        viewModel.Sessions[0].Inputs[1].ShowError.Should().BeTrue();
    }

    [TestMethod]
    public async Task DayDetail_Uncross_AppliedOnSave()
    {
        await _enrollments.CrossAsync(0, 0);
        DayDetailViewModel viewModel = new(_journal, _enrollments, new FakeStringLocalizer(), _preferences, NullLogger<DayDetailViewModel>.Instance);
        await viewModel.LoadAsync(0);

        viewModel.ToggleUncrossCommand.Execute(viewModel.Sessions[0]);
        (await _enrollments.GetCompletionsAsync()).Should().ContainSingle("nothing is written before Save");
        await viewModel.SaveAsync();

        (await _enrollments.GetCompletionsAsync()).Should().BeEmpty();
    }

    [TestMethod]
    public async Task LogResults_Session_PrefillsAndSaves()
    {
        var sheet = (await _journal.GetResultsSheetAsync(0, 0))!;
        await _journal.SaveResultsAsync(sheet.Target, new Dictionary<string, MetricValue> { ["time"] = new(65) });
        LogResultsViewModel viewModel = new(_journal, new FakeStringLocalizer(), _preferences);

        (await viewModel.LoadForSessionAsync(0, 0)).Should().BeTrue();
        viewModel.Inputs[1].Text.Should().Be("1:05");
        viewModel.Inputs[0].Number = 33;
        await viewModel.SaveAsync();

        (await _journal.GetResultsSheetAsync(0, 0))!.Values.Should().BeEquivalentTo(new Dictionary<string, MetricValue> { ["jacks"] = new(33), ["time"] = new(65) });
    }

    [TestMethod]
    public async Task LogResults_WorkoutWithoutMetrics_LoadsNothing()
    {
        LogResultsViewModel viewModel = new(_journal, new FakeStringLocalizer(), _preferences);

        (await viewModel.LoadForSessionAsync(2, 0)).Should().BeFalse();
        viewModel.HasResults.Should().BeFalse();
    }

    [TestMethod]
    public async Task SaveResults_UnknownMetric_Throws()
    {
        var sheet = (await _journal.GetResultsSheetAsync(0, 0))!;

        Func<Task> act = () => _journal.SaveResultsAsync(sheet.Target, new Dictionary<string, MetricValue> { ["burpees"] = new(10) });

        await act.Should().ThrowAsync<ArgumentException>();
        _changes.Should().Be(0);
    }

    [TestMethod]
    public async Task SaveResults_WorkoutNotScheduledThere_Throws()
    {
        var enrollmentId = (await _enrollments.GetAsync())!.Enrollment.Id;

        Func<Task> act = () => _journal.SaveResultsAsync(
            MetricTarget.ForSession(enrollmentId, 2, 0, "fit"),
            new Dictionary<string, MetricValue> { ["jacks"] = new(10) });

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [TestMethod]
    [DataRow(-7, true)]
    [DataRow(-8, false)]
    public async Task GetResults_BonusBeforeDayOne_CountsWithinAWeek(int daysFromStart, bool included)
    {
        Time.SetUtcNow(new DateTimeOffset(2026, 12, 10, 12, 0, 0, TimeSpan.Zero));
        var bonus = await _enrollments.LogBonusAsync("fit", "Fit Test", _start.AddDays(daysFromStart), null);
        await _journal.SaveResultsAsync((await _journal.GetBonusResultsSheetAsync(bonus))!.Target, new Dictionary<string, MetricValue> { ["jacks"] = new(30) });

        var table = (await _journal.GetResultsAsync()).Single();

        table.Columns.Any(c => c.BonusWorkoutId == bonus.Id).Should().Be(included);
    }

    [TestMethod]
    public async Task HasMetrics_ScheduledWorkout_ReflectsTheCatalog()
    {
        (await _journal.HasMetricsAsync(0, 0)).Should().BeTrue();
        (await _journal.HasMetricsAsync(2, 0)).Should().BeFalse();
        (await _journal.HasMetricsAsync(99, 0)).Should().BeFalse();
    }

    [TestMethod]
    public async Task DayDetail_UncrossThenUndoOrCancel_KeepsTheCross()
    {
        await _enrollments.CrossAsync(0, 0);
        DayDetailViewModel viewModel = new(_journal, _enrollments, new FakeStringLocalizer(), _preferences, NullLogger<DayDetailViewModel>.Instance);
        await viewModel.LoadAsync(0);
        var session = viewModel.Sessions[0];

        viewModel.ToggleUncrossCommand.Execute(session);
        session.Should().BeEquivalentTo(new { PendingUncross = true, ShowsCrossed = false, CanUncross = false });
        viewModel.ToggleUncrossCommand.Execute(session);
        session.Should().BeEquivalentTo(new { PendingUncross = false, ShowsCrossed = true, CanUncross = true });

        // Cancel: the sheet is dropped without SaveAsync.
        viewModel.ToggleUncrossCommand.Execute(session);

        (await _enrollments.GetCompletionsAsync()).Should().ContainSingle();
    }

    [TestMethod]
    public async Task DayDetail_UndoneSession_CannotBeMarked()
    {
        DayDetailViewModel viewModel = new(_journal, _enrollments, new FakeStringLocalizer(), _preferences, NullLogger<DayDetailViewModel>.Instance);
        await viewModel.LoadAsync(0);

        viewModel.ToggleUncrossCommand.Execute(viewModel.Sessions[0]);

        viewModel.Sessions[0].PendingUncross.Should().BeFalse();
    }
}
