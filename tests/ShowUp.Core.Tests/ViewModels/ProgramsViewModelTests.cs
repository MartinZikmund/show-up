using System.Globalization;
using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using ShowUp.Core.Catalog;
using ShowUp.Core.Localization;
using ShowUp.Core.Tests.Tracking;
using ShowUp.Core.ViewModels;

namespace ShowUp.Core.Tests.ViewModels;

[TestClass]
public sealed class ProgramsViewModelTests
{
    private readonly RecordingNavigationService _navigation = new();

    [TestMethod]
    public async Task Filter_ByName_IsCaseInsensitive()
    {
        var viewModel = await LoadAsync(Program("insanity", "Insanity", "Insanity"), Program("p90x", "P90X", "P90X"));

        viewModel.Filter = "INSAN";

        viewModel.Programs.Should().ContainSingle().Which.Id.Should().Be("insanity");
    }

    [TestMethod]
    public async Task Filter_NoMatch_FlagsEmptyResult()
    {
        var viewModel = await LoadAsync(Program("insanity", "Insanity", "Insanity"));

        viewModel.Filter = "zzz";

        viewModel.Programs.Should().BeEmpty();
        viewModel.HasNoMatches.Should().BeTrue();
    }

    [TestMethod]
    public async Task SelectedFamily_NarrowsTheList()
    {
        var viewModel = await LoadAsync(Program("insanity", "Insanity", "Insanity"), Program("unhinged", "Unhinged", "Insanity"), Program("p90x", "P90X", "P90X"));

        viewModel.SelectedFamily = "Insanity";

        viewModel.Programs.Select(p => p.Id).Should().BeEquivalentTo(["insanity", "unhinged"]);
    }

    [TestMethod]
    public async Task Families_AreDistinctAndSorted()
    {
        var viewModel = await LoadAsync(Program("a", "A", "P90X"), Program("b", "B", "Insanity"), Program("c", "C", "P90X"));

        viewModel.Families.Should().Equal("Insanity", "P90X");
        viewModel.FamilyOptions.Should().HaveCount(3);
        viewModel.SelectedFamily.Should().BeNull();
    }

    [TestMethod]
    public async Task Card_TotalDays_IsTheLongestTrack()
    {
        var program = Program("p", "P", "F");
        var longer = new ProgramTrack("long", "Long", [new ProgramPhase("Ph", [new ScheduleWeek(Enumerable.Repeat(new ScheduleDay([new ScheduledSession("run")]), 7).ToList())])]);
        program = program with { Tracks = [program.Tracks[0], longer] };

        var viewModel = await LoadAsync(program);

        var card = viewModel.Programs.Single();
        card.TotalDays.Should().Be(7);
        card.TrackCount.Should().Be(2);
    }

    [TestMethod]
    public async Task Open_NavigatesToDetailWithProgramId()
    {
        var viewModel = await LoadAsync(Program("insanity", "Insanity", "Insanity"));

        viewModel.OpenCommand.Execute(viewModel.Programs.Single());

        var navigation = _navigation.Navigations.Should().ContainSingle().Subject;
        navigation.ViewModel.Should().Be(typeof(ProgramDetailViewModel));
        navigation.Parameter.Should().Be("insanity");
    }

    [TestMethod]
    [DataRow("cs", 1, PluralCategory.One)]
    [DataRow("cs", 2, PluralCategory.Few)]
    [DataRow("cs", 4, PluralCategory.Few)]
    [DataRow("cs", 5, PluralCategory.Other)]
    [DataRow("cs", 0, PluralCategory.Other)]
    [DataRow("cs", 100, PluralCategory.Other)]
    [DataRow("en", 1, PluralCategory.One)]
    [DataRow("en", 2, PluralCategory.Other)]
    public void CategoryFor_PicksTheRightForm(string culture, int count, PluralCategory expected) =>
        PluralForms.CategoryFor(CultureInfo.GetCultureInfo(culture), count).Should().Be(expected);

    [TestMethod]
    public async Task Card_Summary_UsesThePluralFormOfTheUiCulture()
    {
        CultureInfo previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("cs");
            var viewModel = await LoadAsync(TestCatalog.Program("p", 1, "run", "run", "run") with { Name = "P" });

            viewModel.Programs.Single().SummaryText.Should().Contain("ProgramDays_Few(3)").And.Contain("ProgramTracks_One(1)");
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }

    [TestMethod]
    public async Task Card_Automation_NamesTheProgramSummaryAndVerification()
    {
        var viewModel = await LoadAsync(Program("insanity", "Insanity", "Insanity"));

        var card = viewModel.Programs.Single();

        card.AutomationId.Should().Be("ProgramCard_insanity");
        card.AutomationName.Should().StartWith("Insanity, ").And.EndWith(", VerificationOfficial");
    }

    [TestMethod]
    public void KeyFor_AppendsTheCategory() =>
        PluralForms.KeyFor(CultureInfo.GetCultureInfo("cs"), "ProgramDays", 3).Should().Be("ProgramDays_Few");

    private static WorkoutProgram Program(string id, string name, string family) =>
        TestCatalog.Program(id, 1, "run", "run") with { Name = name, Family = family };

    private async Task<ProgramsViewModel> LoadAsync(params WorkoutProgram[] programs)
    {
        ProgramsViewModel viewModel = new(new PassThroughLocalizer(), new FakeCatalogService(programs), _navigation, NullLogger<ProgramsViewModel>.Instance);
        await viewModel.LoadAsync();
        return viewModel;
    }
}
