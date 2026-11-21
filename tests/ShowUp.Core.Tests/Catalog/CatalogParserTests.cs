using AwesomeAssertions;
using ShowUp.Core.Catalog;

namespace ShowUp.Core.Tests.Catalog;

[TestClass]
public class CatalogParserTests
{
    [TestMethod]
    public void Parse_SampleProgram_FlattensDaysInOrder()
    {
        var catalog = CatalogParser.Parse(1, Fixture("sample-workouts.json"), [Fixture("sample-program.json")]);

        var track = catalog.Programs.Single().Tracks.Single();

        track.Days.Should().HaveCount(14);
        track.Days[0].Sessions.Select(s => s.WorkoutId).Should().Equal("fit-test");
        track.Days[1].Sessions.Should().HaveCount(2);
    }

    [TestMethod]
    public void Parse_SampleProgram_ReadsEnumsAndDates()
    {
        var catalog = CatalogParser.Parse(3, Fixture("sample-workouts.json"), [Fixture("sample-program.json")]);

        var program = catalog.Programs.Single();

        catalog.Version.Should().Be(3);
        program.Verification.Should().Be(VerificationLevel.Official);
        program.VerifiedOn.Should().Be(new DateOnly(2026, 11, 21));
        catalog.WorkoutsById["fit-test"].Kind.Should().Be(WorkoutKind.FitTest);
        catalog.WorkoutsById["rest"].DurationMinutes.Should().BeNull();
    }

    [TestMethod]
    public void Locate_SecondPhaseFirstDay_ReturnsPhaseAndWeek()
    {
        ScheduleDay day = new([new ScheduledSession("rest")]);
        ProgramTrack track = new("t", "T",
        [
            new ProgramPhase("One", [new ScheduleWeek([day, day, day]), new ScheduleWeek([day, day])]),
            new ProgramPhase("Two", [new ScheduleWeek([day, day, day])]),
        ]);

        track.Locate(5).Should().Be(("Two", 3, 1));
        track.Locate(4).Should().Be(("One", 2, 2));
        track.Locate(0).Should().Be(("One", 1, 1));
    }

    private static string Fixture(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Catalog", "Fixtures", name));
}
