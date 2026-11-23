using AwesomeAssertions;
using ShowUp.Core.Catalog;
using ShowUp.Core.CustomPrograms;
using ShowUp.Core.Storage;

namespace ShowUp.Core.Tests.CustomPrograms;

[TestClass]
public sealed class CustomProgramFactoryTests
{
    [TestMethod]
    public void Create_ThenParse_RoundTripsWithTheRowsIdAndRevision()
    {
        CustomProgramDraft draft = new(
            "custom-a",
            "  Mine  ",
            "#3E8E41",
            [[[new CustomSession("run"), new CustomSession("custom-a-1", Optional: true)], .. Enumerable.Repeat<IReadOnlyList<CustomSession>>([new CustomSession("rest")], 6)]],
            [new Workout("custom-a-1", "Plank", 5, WorkoutKind.Abs, null)]);

        var json = CustomProgramFactory.Serialize(CustomProgramFactory.Create(draft, new DateOnly(2026, 11, 21)));
        var program = CustomProgramFactory.Parse(new CustomProgramEntity { SubjectKey = "c:custom-a", Revision = 42, ProgramJson = json })!;

        program.Id.Should().Be("custom-a");
        program.Revision.Should().Be(42);
        program.IsCustom.Should().BeTrue();
        program.Name.Should().Be("Mine");
        program.Tracks.Should().ContainSingle().Which.Days.Should().HaveCount(7);
        program.Tracks[0].Days[0].Sessions[1].Optional.Should().BeTrue();
        CustomProgramFactory.ToDraft(program).Weeks.Should().BeEquivalentTo(draft.Weeks, o => o.WithStrictOrdering());
        program.Workouts.Should().Equal(draft.Workouts);
    }

    [TestMethod]
    public void Create_LongName_TruncatesTheProgramTrackAndPhaseAlike()
    {
        string name = new('x', CustomProgramFactory.MaxNameLength + 10);
        CustomProgramDraft draft = new("custom-a", name, "#000000", [[.. Enumerable.Repeat<IReadOnlyList<CustomSession>>([new CustomSession("rest")], 7)]], []);

        var program = CustomProgramFactory.Create(draft, new DateOnly(2026, 11, 21));

        program.Name.Should().HaveLength(CustomProgramFactory.MaxNameLength);
        program.Tracks[0].Name.Should().Be(program.Name);
        program.Tracks[0].Phases[0].Name.Should().Be(program.Name);
    }

    [TestMethod]
    public void Parse_Unreadable_ReturnsNull() =>
        CustomProgramFactory.Parse(new CustomProgramEntity { SubjectKey = "c:custom-a", Revision = 1, ProgramJson = "{" }).Should().BeNull();

    [TestMethod]
    public void NextWorkoutId_IsOnePastTheHighest() =>
        CustomProgramFactory.NextWorkoutId("custom-a", [new("custom-a-1", "A", null, WorkoutKind.Abs, null), new("custom-a-7", "B", null, WorkoutKind.Abs, null), new("run", "Run", null, WorkoutKind.Cardio, null)])
            .Should().Be("custom-a-8");

    [TestMethod]
    public void Validate_ListsEveryProblem()
    {
        CustomProgramDraft draft = new("custom-a", "", "#000000", [[[], [new CustomSession("rest")], [], [new CustomSession("rest")], [new CustomSession("rest")], [new CustomSession("rest")], [new CustomSession("rest")]]], []);

        CustomProgramFactory.Validate(draft).Should().Equal(
            new CustomProgramError(CustomProgramProblem.EmptyName),
            new CustomProgramError(CustomProgramProblem.EmptyDay, 1, 1),
            new CustomProgramError(CustomProgramProblem.EmptyDay, 1, 3));
    }

    [TestMethod]
    public void Validate_ProgramJsonOverTheServerLimit_IsTooLarge()
    {
        var workouts = Enumerable.Range(1, 3000).Select(i => new Workout($"custom-a-{i}", new string('x', 100), 5, WorkoutKind.Abs, null)).ToList();
        CustomProgramDraft big = new("custom-a", "Mine", "#000000", [Enumerable.Repeat<IReadOnlyList<CustomSession>>([new CustomSession("rest")], 7).ToList()], workouts);
        CustomProgramDraft small = big with { Workouts = workouts.Take(10).ToList() };

        CustomProgramFactory.Validate(big).Should().Equal(new CustomProgramError(CustomProgramProblem.TooLarge));
        CustomProgramFactory.Validate(small).Should().BeEmpty();
    }

    [TestMethod]
    public void Create_Invalid_Throws()
    {
        CustomProgramDraft draft = new("custom-a", "Mine", "#000000", [], []);

        var act = () => CustomProgramFactory.Create(draft, new DateOnly(2026, 11, 21));

        act.Should().Throw<ArgumentException>();
    }
}
