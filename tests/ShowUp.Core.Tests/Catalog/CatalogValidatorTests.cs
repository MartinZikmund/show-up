using AwesomeAssertions;
using ShowUp.Core.Catalog;

namespace ShowUp.Core.Tests.Catalog;

[TestClass]
public class CatalogValidatorTests
{
    [TestMethod]
    public void Validate_SampleCatalog_ReturnsNoErrors() => Validate(c => c).Should().BeEmpty();

    [TestMethod]
    public void Validate_UnknownWorkoutReference_ReturnsError()
    {
        var errors = Validate(c => WithProgram(c, p => p with
        {
            Tracks = [p.Tracks[0] with { Phases = [new ProgramPhase("P", [new ScheduleWeek(Week(new ScheduledSession("nope")))])] }],
        }));

        errors.Should().ContainSingle(e => e.Contains("nope"));
    }

    [TestMethod]
    public void Validate_UnknownAlternateReference_ReturnsError()
    {
        var errors = Validate(c => WithProgram(c, p => p with
        {
            Tracks = [p.Tracks[0] with { Phases = [new ProgramPhase("P", [new ScheduleWeek(Week(new ScheduledSession("cardio", ["ghost"])))])] }],
        }));

        errors.Should().ContainSingle(e => e.Contains("ghost"));
    }

    [TestMethod]
    public void Validate_CommunityWithOneSource_ReturnsError()
    {
        var errors = Validate(c => WithProgram(c, p => p with { Verification = VerificationLevel.Community }));

        errors.Should().ContainSingle(e => e.Contains("source"));
    }

    [TestMethod]
    public void Validate_OfficialWithoutSource_ReturnsError()
    {
        var errors = Validate(c => WithProgram(c, p => p with { Sources = [] }));

        errors.Should().ContainSingle(e => e.Contains("source"));
    }

    [TestMethod]
    public void Validate_WeekWithEightDays_ReturnsError()
    {
        var day = new ScheduleDay([new ScheduledSession("rest")]);
        var errors = Validate(c => WithProgram(c, p => p with
        {
            Tracks = [p.Tracks[0] with { Phases = [new ProgramPhase("P", [new ScheduleWeek([.. Enumerable.Repeat(day, 8)])])] }],
        }));

        errors.Should().ContainSingle(e => e.Contains("8 days"));
    }

    [TestMethod]
    public void Validate_DuplicateProgramIds_ReturnsError()
    {
        var errors = Validate(c => c with { Programs = [c.Programs[0], c.Programs[0]] });

        errors.Should().ContainSingle(e => e.Contains("Duplicate program id"));
    }

    [TestMethod]
    public void Validate_DuplicateWorkoutIds_ReturnsError()
    {
        var errors = Validate(c => c with { Workouts = [.. c.Workouts, c.Workouts[0]] });

        errors.Should().Contain(e => e.Contains("Duplicate workout id"));
    }

    [TestMethod]
    [DataRow("red")]
    [DataRow("#FFF")]
    [DataRow("#GGGGGG")]
    public void Validate_BadAccentColor_ReturnsError(string color)
    {
        var errors = Validate(c => WithProgram(c, p => p with { AccentColor = color }));

        errors.Should().ContainSingle(e => e.Contains("accent"));
    }

    [TestMethod]
    public void IsRestDay_AllSessionsRest_ReturnsTrue()
    {
        var catalog = Sample();

        new ScheduleDay([new ScheduledSession("rest")]).IsRestDay(catalog.WorkoutsById).Should().BeTrue();
        new ScheduleDay([new ScheduledSession("rest"), new ScheduledSession("abs")]).IsRestDay(catalog.WorkoutsById).Should().BeFalse();
    }

    private static IReadOnlyList<ScheduleDay> Week(ScheduledSession first) =>
        [new ScheduleDay([first]), .. Enumerable.Repeat(new ScheduleDay([new ScheduledSession("rest")]), 6)];

    private static WorkoutCatalog WithProgram(WorkoutCatalog c, Func<WorkoutProgram, WorkoutProgram> edit) =>
        c with { Programs = [edit(c.Programs[0])] };

    private static IReadOnlyList<string> Validate(Func<WorkoutCatalog, WorkoutCatalog> edit) =>
        CatalogValidator.Validate(edit(Sample()));

    private static WorkoutCatalog Sample() => CatalogParser.Parse(
        1,
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Catalog", "Fixtures", "sample-workouts.json")),
        [File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Catalog", "Fixtures", "sample-program.json"))]);
}
