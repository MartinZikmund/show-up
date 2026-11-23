using System.Globalization;
using AwesomeAssertions;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using ShowUp.Core.Catalog;
using ShowUp.Core.Stats;
using ShowUp.Core.Tests.Entitlements;
using ShowUp.Core.ViewModels;

namespace ShowUp.Core.Tests.ViewModels;

[TestClass]
public sealed class StatsViewModelTests
{
    // A Saturday.
    private static readonly DateOnly _today = new(2026, 11, 21);
    private static readonly DateOnly _monday = new(2026, 11, 16);

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 21, 10, 0, 0, TimeSpan.Zero));
    private readonly FakeEntitlementService _entitlements = new() { IsPro = true };
    private readonly RecordingNavigationService _navigation = new();
    private readonly FakeStatsService _stats = new();

    public StatsViewModelTests()
    {
        CultureInfo.CurrentCulture = new CultureInfo("en-US");
        _time.SetLocalTimeZone(TimeZoneInfo.Utc);
    }

    [TestMethod]
    public async Task Free_AsksForTheLimitedHistory_AndShowsTheGate()
    {
        _entitlements.IsPro = false;
        var viewModel = Create();

        await viewModel.LoadAsync();

        _stats.Requests.Should().Equal(false);
        viewModel.ShowGate.Should().BeTrue();
    }

    [TestMethod]
    public async Task Pro_ShowsHeatmapAndComparison()
    {
        var id = Guid.NewGuid();
        _stats.Summary = Summary(
            heatmap: [.. Enumerable.Range(0, 14).Select(i => new HeatDay(_monday.AddDays(i - 7), i == 12 ? 5 : 0))],
            programs: [new ProgramComparison(id, "Insanity", 9, 12, 4, 0.75)]);
        var viewModel = Create();

        await viewModel.LoadAsync();

        _stats.Requests.Should().Equal(true);
        viewModel.ShowGate.Should().BeFalse();
        viewModel.HeatWeeks.Should().HaveCount(2).And.OnlyContain(w => w.Days.Count == 7);
        var program = viewModel.Programs.Should().ContainSingle().Subject;
        program.Should().Be(new StatsProgramViewModel(id, "Insanity", 0.75, 0.75.ToString("P0", CultureInfo.CurrentCulture), "StatsProgramDetail(9,12,4)"));
    }

    [TestMethod]
    public async Task HeatCell_LevelIsCappedAtThree_AndItsNameSaysTheCount()
    {
        // Saturday the 21st has five workouts; Sunday the 22nd hasn't come yet.
        _stats.Summary = Summary(heatmap: [.. Enumerable.Range(0, 7).Select(i => new HeatDay(_monday.AddDays(i), i == 5 ? 5 : 0))]);
        var viewModel = Create();

        await viewModel.LoadAsync();

        var days = viewModel.HeatWeeks.Single().Days;
        days[5].Should().Be(new StatsHeatCellViewModel(3, IsFuture: false, "StatsDayName(November 21,5)"));
        days[6].IsFuture.Should().BeTrue();
        days[0].Level.Should().Be(0);
    }

    [TestMethod]
    public async Task WeekBars_ScaleToTheBusiestWeek()
    {
        _stats.Summary = Summary(weeks: [new(_monday.AddDays(-14), 0, 0), new(_monday.AddDays(-7), 2, 60), new(_monday, 4, 150)]);
        var viewModel = Create();

        await viewModel.LoadAsync();

        viewModel.WeekBars.Select(b => b.Fraction).Should().Equal(0, 0.5, 1);
        viewModel.WeekBars[^1].Should().Be(new StatsBarViewModel("Nov 16", 1, "4", "StatsWeekName(November 16,4,150)"));
    }

    [TestMethod]
    public async Task WeekBars_AllEmpty_HaveNoLength()
    {
        _stats.Summary = Summary(weeks: [new(_monday.AddDays(-7), 0, 0), new(_monday, 0, 0)]);
        var viewModel = Create();

        await viewModel.LoadAsync();

        viewModel.WeekBars.Should().HaveCount(2).And.OnlyContain(b => b.Fraction == 0);
        viewModel.ShowEmptyState.Should().BeTrue();
    }

    [TestMethod]
    public async Task KindBars_NameTheKindAndScaleByMinutes()
    {
        _stats.Summary = Summary(byKind: [new(WorkoutKind.Cardio, 3, 90), new(WorkoutKind.Abs, 2, 30)]) with { TotalWorkouts = 5, TotalMinutes = 120 };
        var viewModel = Create();

        await viewModel.LoadAsync();

        viewModel.KindBars.Should().Equal(
            new StatsBarViewModel("WorkoutKind_Cardio", 1, "StatsHoursMinutes(1,30)", "StatsKindName(WorkoutKind_Cardio,3,90)"),
            new StatsBarViewModel("WorkoutKind_Abs", 30d / 90, "StatsMinutes(30)", "StatsKindName(WorkoutKind_Abs,2,30)"));
        viewModel.HasKinds.Should().BeTrue();
        viewModel.ShowEmptyState.Should().BeFalse();
    }

    [TestMethod]
    public async Task Totals_AreFormatted()
    {
        _stats.Summary = Summary() with { TotalWorkouts = 1234, TotalMinutes = 150, ActiveDays = 40 };
        var viewModel = Create();

        await viewModel.LoadAsync();

        (viewModel.TotalWorkoutsText, viewModel.TotalTimeText, viewModel.ActiveDaysText).Should().Be(("1,234", "StatsHoursMinutes(2,30)", "40"));
    }

    [TestMethod]
    public async Task ProLapses_NextLoadAsksForTheLimitedHistory()
    {
        var viewModel = Create();
        await viewModel.LoadAsync();
        _entitlements.IsPro = false;

        await viewModel.LoadAsync();

        _stats.Requests.Should().Equal(true, false);
        viewModel.ShowGate.Should().BeTrue();
    }

    [TestMethod]
    public void OpenPro_NavigatesToThePaywall()
    {
        Create().OpenProCommand.Execute(null);

        _navigation.Navigations.Should().ContainSingle().Which.Should().Be((typeof(ProViewModel), (object?)null));
    }

    private StatsViewModel Create() =>
        new(_stats, _entitlements, _navigation, new WeakReferenceMessenger(), new PassThroughLocalizer(), _time, NullLogger<StatsViewModel>.Instance);

    private static StatsSummary Summary(
        WeekBucket[]? weeks = null,
        KindBucket[]? byKind = null,
        HeatDay[]? heatmap = null,
        ProgramComparison[]? programs = null) =>
        new(0, 0, 0, weeks ?? [], byKind ?? [], heatmap ?? [], programs ?? []);

    private sealed class FakeStatsService : IStatsService
    {
        public StatsSummary Summary { get; set; } = new(0, 0, 0, [], [], [], []);

        public List<bool> Requests { get; } = [];

        public Task<StatsSummary> GetAsync(bool fullHistory)
        {
            Requests.Add(fullHistory);
            return Task.FromResult(Summary);
        }
    }
}
