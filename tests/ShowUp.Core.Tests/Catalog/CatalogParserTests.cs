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
    public void TryParseProgram_Valid_ReturnsProgram()
    {
        bool ok = CatalogParser.TryParseProgram(Fixture("sample-program.json"), out var program, out var error);

        ok.Should().BeTrue();
        error.Should().BeNull();
        program!.Id.Should().Be("sample");
    }

    [TestMethod]
    public void TryParseProgram_MissingTracks_ReturnsError()
    {
        string json = Fixture("sample-program.json").Replace("\"tracks\"", "\"extra\"");

        bool ok = CatalogParser.TryParseProgram(json, out var program, out var error);

        ok.Should().BeFalse();
        program.Should().BeNull();
        error.Should().NotBeNullOrEmpty();
    }

    [TestMethod]
    public void TryParseProgram_UnknownProperty_ReturnsError()
    {
        string json = Fixture("sample-program.json").Replace("\"id\": \"sample\",", "\"id\": \"sample\", \"surprise\": 1,");

        bool ok = CatalogParser.TryParseProgram(json, out _, out var error);

        ok.Should().BeFalse();
        error.Should().Contain("surprise");
    }

    [TestMethod]
    public void TryParseProgram_NullTracks_ReturnsError()
    {
        string json = """{"id":"p","revision":1,"name":"n","family":"f","description":"d","heroImage":null,"accentColor":"#FFFFFF","verification":"official","sources":[],"verifiedOn":"2026-01-01","tracks":null}""";

        CatalogParser.TryParseProgram(json, out _, out _).Should().BeFalse();
    }

    [TestMethod]
    public void Parse_MalformedProgram_Throws()
    {
        Action act = () => CatalogParser.Parse(1, Fixture("sample-workouts.json"), ["""{"id":"p"}"""]);

        act.Should().Throw<System.Text.Json.JsonException>();
    }

    [TestMethod]
    public void ParseWorkouts_Sample_ReturnsWorkouts() =>
        CatalogParser.ParseWorkouts(Fixture("sample-workouts.json")).Should().NotBeEmpty();

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

    [TestMethod]
    public void Parse_SessionWithoutOptionalKey_DefaultsToRequired()
    {
        var catalog = CatalogParser.Parse(1, Fixture("sample-workouts.json"), [Fixture("sample-program.json")]);

        catalog.Programs.Single().Tracks.Single().Days.SelectMany(d => d.Sessions).Should().OnlyContain(s => !s.Optional);
    }
}
