using AwesomeAssertions;
using ShowUp.Core.Marking;
using ShowUp.Core.Tests.Tracking;
using ShowUp.Core.Tests.ViewModels;
using ShowUp.Core.Tracking;

namespace ShowUp.Core.Tests.Marking;

[TestClass]
public sealed class CrossingCoordinatorTests
{
    private static readonly DateOnly _today = new(2026, 11, 21);

    private readonly FakeAppPreferences _preferences = new() { CrossOffStyle = CrossOffStyle.AutoMarker, CrossMomentMode = CrossMomentMode.Always };
    private readonly FakeCrossMoment _crossMoment = new();
    private readonly RecordingEnrollmentService _enrollments = new();
    private readonly StubJournalService _journal = new();
    private readonly RecordingJournalDialogs _journalDialogs = new();
    private readonly List<string> _log = [];

    [TestMethod]
    public async Task Cross_FirstOfDay_MomentUsesSeedOfTheWrittenCompletion()
    {
        var coordinator = Create();

        await coordinator.CrossAsync(Target(), Hooks());

        var written = _enrollments.Crosses.Should().ContainSingle().Subject;
        _crossMoment.Shown.Should().ContainSingle().Which.Seed.Should().Be(MarkerGeometry.SeedFrom(written.CompletionId!.Value));
    }

    [TestMethod]
    public async Task Cross_LaterOfDay_KeepsTheDaySeed()
    {
        var coordinator = Create();
        CompletedSession first = new(0, 0, Guid.NewGuid(), _today);

        await coordinator.CrossAsync(Target(session: 1, completions: [first], daySeed: 4242, done: [0]), Hooks());

        _crossMoment.Shown.Should().ContainSingle().Which.Seed.Should().Be(4242);
    }

    [TestMethod]
    public async Task Cross_Auto_WritesWithoutInkAndHoldsMarksStillForTheMoment()
    {
        var coordinator = Create();

        var crossed = await coordinator.CrossAsync(Target(), Hooks());

        crossed.Should().BeTrue();
        _enrollments.Crosses.Should().ContainSingle().Which.Ink.Should().BeNull();
        _log.Should().Equal("animate:False", "write", "refresh");
    }

    [TestMethod]
    public async Task Cross_AutoWithoutMoment_AnimatesMarksOnTheCell()
    {
        _preferences.CrossMomentMode = CrossMomentMode.Never;
        var coordinator = Create();

        await coordinator.CrossAsync(Target(), Hooks());

        _crossMoment.Shown.Should().BeEmpty();
        _log.Should().Equal("animate:True", "write", "refresh");
    }

    [TestMethod]
    public async Task Cross_BackfilledDay_SkipsTheMomentUnderOnlyToday()
    {
        _preferences.CrossMomentMode = CrossMomentMode.OnlyToday;
        var coordinator = Create();

        await coordinator.CrossAsync(Target(isToday: false), Hooks());

        _crossMoment.Shown.Should().BeEmpty();
    }

    [TestMethod]
    public async Task Cross_DrawCancelled_WritesNothing()
    {
        _preferences.CrossOffStyle = CrossOffStyle.DrawYourself;
        _crossMoment.DrawResult = CrossMomentResult.Cancelled;
        var coordinator = Create();

        var crossed = await coordinator.CrossAsync(Target(), Hooks());

        crossed.Should().BeFalse();
        _enrollments.Crosses.Should().BeEmpty();
        _log.Should().BeEmpty();
    }

    [TestMethod]
    public async Task Cross_Drawn_WritesTheInkWithTheMomentsSeed()
    {
        _preferences.CrossOffStyle = CrossOffStyle.DrawYourself;
        _crossMoment.DrawResult = CrossMomentResult.Drawn("[[0,0,1,1]]");
        var coordinator = Create();

        await coordinator.CrossAsync(Target(), Hooks());

        var written = _enrollments.Crosses.Should().ContainSingle().Subject;
        written.Ink.Should().Be("[[0,0,1,1]]");
        _crossMoment.DrawRequests.Should().ContainSingle().Which.Info.Seed.Should().Be(MarkerGeometry.SeedFrom(written.CompletionId!.Value));
        _log.Should().Equal("animate:False", "write", "refresh");
    }

    [TestMethod]
    public async Task Cross_DrawWithEarlierInk_PassesItAsPriorInk()
    {
        _preferences.CrossOffStyle = CrossOffStyle.DrawYourself;
        var coordinator = Create();
        CompletedSession drawn = new(0, 0, Guid.NewGuid(), _today, "[[1,1,2,2]]");

        await coordinator.CrossAsync(Target(session: 1, completions: [drawn], done: [0]), Hooks());

        _crossMoment.DrawRequests.Should().ContainSingle().Which.PriorInks.Should().Equal("[[1,1,2,2]]");
    }

    [TestMethod]
    public async Task LogResults_WorkoutWithoutMetrics_NotOffered()
    {
        var coordinator = Create();

        await coordinator.CrossAsync(Target(), Hooks());

        _crossMoment.Shown.Should().ContainSingle().Which.LogResults.Should().BeNull();
    }

    [TestMethod]
    public async Task LogResults_WorkoutWithMetrics_OpensTheSessionsResults()
    {
        _journal.SheetFor = (day, session) => day == 0 && session == 1
            ? new ResultsSheet(TestCatalog.Workouts.Single(w => w.Id == "fit"), MetricTarget.ForSession(Guid.NewGuid(), 0, 1, "fit"), _today, 1, new Dictionary<string, MetricValue>())
            : null;
        var coordinator = Create();

        await coordinator.CrossAsync(Target(session: 1, done: [0]), Hooks());
        await _crossMoment.Shown.Single().LogResults!();

        _journalDialogs.Calls.Should().Equal("results:0:1");
    }

    [TestMethod]
    public async Task LogResults_DrawMode_OfferedOnTheDrawMoment()
    {
        _preferences.CrossOffStyle = CrossOffStyle.DrawYourself;
        _journal.SheetFor = (_, _) => new ResultsSheet(TestCatalog.Workouts.Single(w => w.Id == "fit"), MetricTarget.ForSession(Guid.NewGuid(), 0, 0, "fit"), _today, 1, new Dictionary<string, MetricValue>());
        var coordinator = Create();

        await coordinator.CrossAsync(Target(), Hooks());

        _crossMoment.DrawRequests.Should().ContainSingle().Which.Info.LogResults.Should().NotBeNull();
    }

    private CrossingCoordinator Create() => new(_preferences, _crossMoment, new FakeMarkingFeedback(), _enrollments, _journal, _journalDialogs);

    private CrossHooks Hooks() => new(
        async write =>
        {
            await write();
            _log.Add("write");
        },
        () =>
        {
            _log.Add("refresh");
            return Task.CompletedTask;
        },
        animate => _log.Add($"animate:{animate}"));

    private static CrossTarget Target(int session = 0, bool isToday = true, int daySeed = 0, int[]? done = null, CompletedSession[]? completions = null) =>
        new(
            0,
            session,
            "Run",
            isToday,
            daySeed,
            new DayStatus(0, _today, 2, new HashSet<int>(done ?? []), IsRestDay: false),
            [new DayStatus(0, _today, 2, new HashSet<int>(done ?? []), IsRestDay: false)],
            completions ?? [],
            _today);
}
