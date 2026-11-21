using AwesomeAssertions;
using ShowUp.Core.Catalog;
using ShowUp.Core.ViewModels;

namespace ShowUp.Core.Tests.ViewModels;

[TestClass]
public sealed class BonusWorkoutDialogViewModelTests
{
    private static readonly DateOnly _today = new(2026, 11, 21);

    private static readonly Workout[] _workouts =
    [
        new("insanity-max-30-ab-attack-10", "Ab Attack: 10", 10, WorkoutKind.Abs, "Insanity Max:30"),
        new("cardio-challenge", "Cardio Challenge", 30, WorkoutKind.Cardio, "Insanity"),
        new("yoga", "Cardío Yoga", 45, WorkoutKind.Yoga, "Other"),
        new("rest", "Rest", null, WorkoutKind.Rest, null),
    ];

    [TestMethod]
    public void Dialog_Search_AbAttack_FindsMax30Workout()
    {
        var viewModel = Create();

        viewModel.Query = "ab att";

        viewModel.Suggestions.Should().ContainSingle().Which.Id.Should().Be("insanity-max-30-ab-attack-10");
        viewModel.Suggestions[0].Family.Should().Be("Insanity Max:30");
    }

    [TestMethod]
    public void Search_DiacriticsAndCase_AreIgnored()
    {
        var viewModel = Create();

        viewModel.Query = "CARDIO";

        viewModel.Suggestions.Select(s => s.Id).Should().BeEquivalentTo(["cardio-challenge", "yoga"]);
    }

    [TestMethod]
    public void Search_RestIsNotAWorkout()
    {
        var viewModel = Create();

        viewModel.Query = "rest";

        viewModel.Suggestions.Should().BeEmpty();
    }

    [TestMethod]
    public void Search_ManyMatches_CapsAtTwenty()
    {
        var many = Enumerable.Range(0, 40).Select(i => new Workout($"w{i}", $"Workout {i}", 10, WorkoutKind.Mixed, null));
        BonusWorkoutDialogViewModel viewModel = new(many, _today, _today);

        viewModel.Query = "work";

        viewModel.Suggestions.Should().HaveCount(20);
    }

    [TestMethod]
    public void Select_Workout_PrefillsNameAndDuration()
    {
        var viewModel = Create();

        viewModel.Selected = _workouts[0];

        viewModel.Query.Should().Be("Ab Attack: 10");
        viewModel.DurationMinutes.Should().Be(10);
        viewModel.CanSave.Should().BeTrue();
        viewModel.ToEntry().WorkoutId.Should().Be("insanity-max-30-ab-attack-10");
    }

    [TestMethod]
    public void Query_EditedAfterSelect_DropsSelection()
    {
        var viewModel = Create();
        viewModel.Selected = _workouts[0];

        viewModel.Query = "My own thing";

        viewModel.Selected.Should().BeNull();
        viewModel.CustomTitle.Should().Be("My own thing");
        viewModel.ToEntry().WorkoutId.Should().BeNull();
    }

    [TestMethod]
    public void CanSave_BlankTitle_IsFalse()
    {
        var viewModel = Create();

        viewModel.Query = "   ";

        viewModel.CanSave.Should().BeFalse();
    }

    [TestMethod]
    public void Dialog_FutureDate_CannotSave()
    {
        var viewModel = Create();
        viewModel.Query = "Run";

        viewModel.Date = _today.AddDays(1);

        viewModel.CanSave.Should().BeFalse();
    }

    [TestMethod]
    public void CanSave_PastDate_IsTrue()
    {
        var viewModel = Create();
        viewModel.Query = "Run";

        viewModel.Date = _today.AddDays(-9);

        viewModel.CanSave.Should().BeTrue();
    }

    [TestMethod]
    public void CanSave_ZeroMinutes_IsFalse()
    {
        var viewModel = Create();
        viewModel.Query = "Run";

        viewModel.DurationMinutes = 0;

        viewModel.CanSave.Should().BeFalse();
    }

    [TestMethod]
    public void SetDate_Cleared_FallsBackToToday()
    {
        var viewModel = Create();
        viewModel.Query = "Run";
        viewModel.Date = _today.AddDays(-3);

        viewModel.SetDate(null);

        viewModel.Date.Should().Be(_today);
        viewModel.CanSave.Should().BeTrue();
    }

    [TestMethod]
    public void CanSave_DurationOverTenHours_IsFalse()
    {
        var viewModel = Create();
        viewModel.Query = "Run";

        viewModel.DurationMinutes = 601;

        viewModel.CanSave.Should().BeFalse();
    }

    [TestMethod]
    public void Query_LongerThanLimit_IsCut()
    {
        var viewModel = Create();

        viewModel.Query = new string('x', 120);

        viewModel.Query.Should().HaveLength(BonusWorkoutDialogViewModel.MaxTitleLength);
    }

    private static BonusWorkoutDialogViewModel Create() => new(_workouts, _today, _today);
}
