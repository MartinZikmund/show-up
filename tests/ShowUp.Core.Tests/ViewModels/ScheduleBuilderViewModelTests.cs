using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using ShowUp.Core.Catalog;
using ShowUp.Core.CustomPrograms;
using ShowUp.Core.Services;
using ShowUp.Core.Tests.CustomPrograms;
using ShowUp.Core.Tests.Entitlements;
using ShowUp.Core.Tests.Tracking;
using ShowUp.Core.ViewModels;

namespace ShowUp.Core.Tests.ViewModels;

[TestClass]
public sealed class ScheduleBuilderViewModelTests
{
    private readonly FakeCustomProgramService _programs = new();
    private readonly FakeEntitlementService _entitlements = new() { IsPro = true };
    private readonly RecordingNavigationService _navigation = new();
    private readonly FakeSessionPicker _picker = new();
    private readonly RecordingDialogService _dialogs = new();
    private readonly StubConfirmationDialogService _confirmation = new();

    [TestMethod]
    public async Task Save_EmptyDay_Blocked()
    {
        var viewModel = await NewAsync();
        viewModel.Name = "Mine";
        FillWeek(viewModel.Weeks[0]);

        viewModel.Weeks[0].Days[3].ClearCommand.Execute(null);

        viewModel.CanSave.Should().BeFalse();
        viewModel.SaveCommand.CanExecute(null).Should().BeFalse();
        viewModel.ValidationMessages.Should().ContainSingle().Which.Should().Contain("BuilderErrorEmptyDays");
        viewModel.Weeks[0].HasEmptyDays.Should().BeTrue();
        await viewModel.SaveAsync();
        _programs.Saved.Should().BeEmpty();
    }

    [TestMethod]
    public async Task Save_ZeroWeeks_Blocked()
    {
        var viewModel = await NewAsync();
        viewModel.Name = "Mine";
        FillWeek(viewModel.Weeks[0]);

        viewModel.WeekCountValue = double.NaN;

        viewModel.WeekCount.Should().Be(0);
        viewModel.Weeks.Should().BeEmpty();
        viewModel.WeeksError.Should().Be("BuilderErrorNoWeeks");
        viewModel.CanSave.Should().BeFalse();
    }

    [TestMethod]
    public async Task Save_TooManyWeeks_Blocked()
    {
        var viewModel = await NewAsync();
        viewModel.Name = "Mine";

        viewModel.WeekCount = 27;

        viewModel.Weeks.Should().HaveCount(CustomProgramFactory.MaxWeeks);
        viewModel.WeeksError.Should().StartWith("BuilderErrorTooManyWeeks");
        viewModel.CanSave.Should().BeFalse();
    }

    [TestMethod]
    public async Task Save_EmptyName_Blocked()
    {
        var viewModel = await NewAsync();
        FillWeek(viewModel.Weeks[0]);

        viewModel.Name = "   ";

        viewModel.NameError.Should().Be("BuilderErrorName");
        viewModel.CanSave.Should().BeFalse();
    }

    [TestMethod]
    public async Task Free_OpenBuilder_ShowsGate()
    {
        _entitlements.IsPro = false;
        _programs.CanEdit = false;

        var viewModel = await NewAsync();
        viewModel.Name = "Mine";
        FillWeek(viewModel.Weeks[0]);

        viewModel.IsLocked.Should().BeTrue();
        viewModel.IsEditable.Should().BeFalse();
        viewModel.CanSave.Should().BeFalse();
        viewModel.OpenProCommand.Execute(null);
        _navigation.Navigations.Should().ContainSingle().Which.ViewModel.Should().Be(typeof(ProViewModel));
    }

    [TestMethod]
    public async Task Save_Valid_SavesTheDraftAndOpensItsDetail()
    {
        var viewModel = await NewAsync();
        viewModel.Name = " Mine ";
        viewModel.SelectAccentCommand.Execute(viewModel.AccentSwatches[5]);
        FillWeek(viewModel.Weeks[0]);

        await viewModel.SaveAsync();

        var draft = _programs.Saved.Should().ContainSingle().Subject;
        draft.ProgramId.Should().Be(viewModel.ProgramId).And.StartWith("custom-");
        draft.Name.Should().Be(" Mine ");
        draft.AccentColor.Should().Be(ScheduleBuilderViewModel.Palette[5].Hex);
        draft.Weeks.Should().ContainSingle().Which.Should().HaveCount(7);
        _navigation.BackCount.Should().Be(1);
        _navigation.Navigations.Should().ContainSingle().Which.Should().Be((typeof(ProgramDetailViewModel), (object?)viewModel.ProgramId));
    }

    [TestMethod]
    public async Task Save_LostProMeanwhile_LocksInsteadOfFailing()
    {
        var viewModel = await NewAsync();
        viewModel.Name = "Mine";
        FillWeek(viewModel.Weeks[0]);
        _programs.CanEdit = false;

        await viewModel.SaveAsync();

        viewModel.IsLocked.Should().BeTrue();
        _navigation.BackCount.Should().Be(0);
    }

    [TestMethod]
    public async Task Save_Fails_ShowsAnErrorAndStays()
    {
        var viewModel = await NewAsync();
        viewModel.Name = "Mine";
        FillWeek(viewModel.Weeks[0]);
        _programs.SaveFailure = new IOException("disk");

        await viewModel.SaveAsync();

        _dialogs.Shown.Should().ContainSingle().Which.Title.Should().Be("BuilderSaveFailedTitle");
        _navigation.BackCount.Should().Be(0);
    }

    [TestMethod]
    public async Task AddWorkout_TypedTitle_AddsAnOwnWorkoutWithAProgramScopedId()
    {
        var viewModel = await NewAsync();
        _picker.Next = new SessionPick(null, "Hill sprints", 25, WorkoutKind.Cardio, Optional: true);

        await viewModel.Weeks[0].Days[0].AddWorkoutCommand.ExecuteAsync(null);
        _picker.Next = new SessionPick(null, "Plank", null, WorkoutKind.Abs, Optional: false);
        await viewModel.Weeks[0].Days[1].AddWorkoutCommand.ExecuteAsync(null);

        var draft = viewModel.ToDraft();
        draft.Workouts.Select(w => w.Id).Should().Equal($"{viewModel.ProgramId}-1", $"{viewModel.ProgramId}-2");
        draft.Workouts[0].Should().Be(new Workout($"{viewModel.ProgramId}-1", "Hill sprints", 25, WorkoutKind.Cardio, null));
        draft.Weeks[0][0].Should().Equal(new CustomSession($"{viewModel.ProgramId}-1", Optional: true));
        viewModel.Weeks[0].Days[0].Sessions.Single().Detail.Should().Contain("25");
    }

    [TestMethod]
    public async Task AddWorkout_OffersCatalogAndOwnWorkouts_NotOtherCustomPrograms()
    {
        Workout foreign = new("custom-other-1", "Not mine", 10, WorkoutKind.Cardio, null);
        var viewModel = await NewAsync(new FakeCatalogServiceWith([.. TestCatalog.Workouts, foreign]));
        _picker.Next = new SessionPick(null, "Mine", null, WorkoutKind.Mixed, false);
        await viewModel.Weeks[0].Days[0].AddWorkoutCommand.ExecuteAsync(null);

        await viewModel.Weeks[0].Days[1].AddWorkoutCommand.ExecuteAsync(null);

        _picker.Offered!.Select(w => w.Id).Should().Contain(["run", $"{viewModel.ProgramId}-1"]).And.NotContain("custom-other-1");
    }

    [TestMethod]
    public async Task AddWorkout_OnARestDay_ReplacesTheRest()
    {
        var viewModel = await NewAsync();
        var day = viewModel.Weeks[0].Days[0];
        day.MakeRestCommand.Execute(null);
        _picker.Next = new SessionPick(TestCatalog.Workouts[0], "Run", 30, WorkoutKind.Cardio, false);

        await day.AddWorkoutCommand.ExecuteAsync(null);

        day.Sessions.Select(s => s.WorkoutId).Should().Equal("run");
        day.IsRest.Should().BeFalse();
    }

    [TestMethod]
    public async Task MakeRest_DayWithWorkouts_LeavesOnlyRest()
    {
        var viewModel = await NewAsync();
        var day = viewModel.Weeks[0].Days[0];
        await AddAsync(day, "run");

        day.MakeRestCommand.Execute(null);

        day.Sessions.Should().ContainSingle().Which.IsRest.Should().BeTrue();
        day.IsRest.Should().BeTrue();
        viewModel.ToDraft().Weeks[0][0].Should().Equal(new CustomSession("rest"));
    }

    [TestMethod]
    public async Task MoveDown_FirstSession_SwapsWithTheNext()
    {
        var viewModel = await NewAsync();
        var day = viewModel.Weeks[0].Days[0];
        await AddAsync(day, "run");
        await AddAsync(day, "abs");

        day.Sessions[0].CanMoveUp.Should().BeFalse();
        day.Sessions[0].MoveDownCommand.Execute(null);

        day.Sessions.Select(s => s.WorkoutId).Should().Equal("abs", "run");
        day.Sessions[0].CanMoveUp.Should().BeFalse();
        day.Sessions[1].CanMoveDown.Should().BeFalse();
        day.Sessions[1].MoveUpCommand.Execute(null);
        day.Sessions.Select(s => s.WorkoutId).Should().Equal("run", "abs");
    }

    [TestMethod]
    public async Task ToggleOptional_MarksTheSession()
    {
        var viewModel = await NewAsync();
        var day = viewModel.Weeks[0].Days[0];
        await AddAsync(day, "run");
        await AddAsync(day, "abs");

        day.Sessions[1].ToggleOptionalCommand.Execute(null);

        viewModel.ToDraft().Weeks[0][0].Should().Equal(new CustomSession("run"), new CustomSession("abs", Optional: true));
    }

    [TestMethod]
    public async Task CopyToNext_CopiesTheDaysIntoTheFollowingWeekOnly()
    {
        var viewModel = await NewAsync();
        viewModel.WeekCount = 3;
        FillWeek(viewModel.Weeks[0]);

        viewModel.Weeks[0].CopyToNextCommand.Execute(null);

        var draft = viewModel.ToDraft();
        draft.Weeks[1].Should().BeEquivalentTo(draft.Weeks[0], o => o.WithStrictOrdering());
        draft.Weeks[2].Should().OnlyContain(d => d.Count == 0);
        viewModel.Weeks[2].CanCopyToNext.Should().BeFalse();
    }

    [TestMethod]
    public async Task CopyToAll_CopiesIntoEveryOtherWeek()
    {
        var viewModel = await NewAsync();
        viewModel.WeekCount = 3;
        FillWeek(viewModel.Weeks[1]);

        await viewModel.Weeks[1].CopyToAllCommand.ExecuteAsync(null);

        var draft = viewModel.ToDraft();
        draft.Weeks[0].Should().BeEquivalentTo(draft.Weeks[1], o => o.WithStrictOrdering());
        draft.Weeks[2].Should().BeEquivalentTo(draft.Weeks[1], o => o.WithStrictOrdering());
    }

    [TestMethod]
    public async Task WeekCount_StepDownAndBackUp_KeepsTheWeek()
    {
        var viewModel = await NewAsync();
        viewModel.WeekCount = 2;
        FillWeek(viewModel.Weeks[1]);
        viewModel.SelectedWeek = viewModel.Weeks[1];

        viewModel.WeekCount = 1;
        viewModel.SelectedWeek.Should().Be(viewModel.Weeks[0]);
        viewModel.WeekCount = 2;

        viewModel.ToDraft().Weeks[1].Should().OnlyContain(d => d.Count == 1);
    }

    [TestMethod]
    public async Task Load_ExistingCustomProgram_EditsItAndKeepsItsId()
    {
        var program = CustomProgramFactory.Create(
            new CustomProgramDraft("custom-abc", "Saved", "#2F6FDB", [Week("custom-abc-1"), Week("run")], [new Workout("custom-abc-1", "Own", 10, WorkoutKind.Yoga, null)]),
            new DateOnly(2026, 11, 21)) with { Revision = 5 };
        var viewModel = Create(new FakeCatalogServiceWith([.. TestCatalog.Workouts, .. program.Workouts!], program));

        await viewModel.LoadAsync("custom-abc");
        await viewModel.SaveAsync();

        viewModel.IsEditing.Should().BeTrue();
        viewModel.Name.Should().Be("Saved");
        viewModel.SelectedAccent!.Hex.Should().Be("#2F6FDB");
        viewModel.WeekCount.Should().Be(2);
        viewModel.Weeks[0].Days[0].Sessions.Single().Title.Should().Be("Own");
        var draft = _programs.Saved.Should().ContainSingle().Subject;
        draft.ProgramId.Should().Be("custom-abc");
        draft.Workouts.Should().ContainSingle(w => w.Id == "custom-abc-1");
        _navigation.Navigations.Should().BeEmpty("an edit just goes back to the detail page");
        _navigation.BackCount.Should().Be(1);
    }

    [TestMethod]
    public async Task Session_ButtonsAreNamedAfterTheWorkout()
    {
        var viewModel = await NewAsync();
        var day = viewModel.Weeks[0].Days[1];
        await AddAsync(day, "run");
        await AddAsync(day, "abs");

        var abs = day.Sessions[1];

        abs.MoveUpName.Should().Be("BuilderMoveUpFormat(Abs)");
        abs.MoveDownName.Should().Be("BuilderMoveDownFormat(Abs)");
        abs.MoreName.Should().Be("BuilderSessionMoreFormat(Abs)");
        (abs.UpAutomationId, abs.DownAutomationId, abs.MoreAutomationId).Should().Be(("BuilderSession_W1D2S2_Up", "BuilderSession_W1D2S2_Down", "BuilderSession_W1D2S2_More"));
        abs.MoveUpCommand.Execute(null);
        abs.UpAutomationId.Should().Be("BuilderSession_W1D2S1_Up");
    }

    [TestMethod]
    public async Task CopyToAll_Declined_LeavesTheOtherWeeksAlone()
    {
        var viewModel = await NewAsync();
        viewModel.WeekCount = 2;
        FillWeek(viewModel.Weeks[0]);
        _confirmation.Result = ShowUp.Services.Dialogs.ConfirmationResult.Denied;

        await viewModel.Weeks[0].CopyToAllCommand.ExecuteAsync(null);

        _confirmation.Asked.Should().ContainSingle().Which.Title.Should().Be("BuilderCopyAllTitle(1)");
        viewModel.ToDraft().Weeks[1].Should().OnlyContain(d => d.Count == 0);
    }

    [TestMethod]
    public async Task CopyToAll_OneWeek_IsDisabledUntilThereAreTwo()
    {
        var viewModel = await NewAsync();

        viewModel.Weeks[0].CanCopyToAll.Should().BeFalse();
        await viewModel.Weeks[0].CopyToAllCommand.ExecuteAsync(null);
        viewModel.WeekCount = 2;

        _confirmation.Asked.Should().BeEmpty();
        viewModel.Weeks[0].CanCopyToAll.Should().BeTrue();
    }

    [TestMethod]
    public async Task CopyToNext_ThenEditingTheSource_LeavesTheCopyAlone()
    {
        var viewModel = await NewAsync();
        viewModel.WeekCount = 2;
        await AddAsync(viewModel.Weeks[0].Days[0], "run");
        viewModel.Weeks[0].CopyToNextCommand.Execute(null);

        await AddAsync(viewModel.Weeks[0].Days[0], "abs");
        viewModel.Weeks[0].Days[0].Sessions[0].ToggleOptionalCommand.Execute(null);

        viewModel.ToDraft().Weeks[1][0].Should().Equal(new CustomSession("run"));
    }

    private static IReadOnlyList<IReadOnlyList<CustomSession>> Week(string first) =>
        [[new CustomSession(first)], .. Enumerable.Repeat<IReadOnlyList<CustomSession>>([new CustomSession("rest")], 6)];

    private static void FillWeek(BuilderWeekViewModel week)
    {
        foreach (var day in week.Days)
        {
            day.MakeRestCommand.Execute(null);
        }
    }

    private async Task AddAsync(BuilderDayViewModel day, string workoutId)
    {
        _picker.Next = new SessionPick(TestCatalog.Workouts.Single(w => w.Id == workoutId), workoutId, null, WorkoutKind.Cardio, false);
        await day.AddWorkoutCommand.ExecuteAsync(null);
    }

    private async Task<ScheduleBuilderViewModel> NewAsync(ICatalogService? catalog = null)
    {
        var viewModel = Create(catalog ?? new FakeCatalogService());
        await viewModel.LoadAsync(null);
        return viewModel;
    }

    private ScheduleBuilderViewModel Create(ICatalogService catalog) =>
        new(new PassThroughLocalizer(), catalog, _programs, _entitlements, _navigation, _picker, _dialogs, _confirmation, NullLogger<ScheduleBuilderViewModel>.Instance);

    private sealed class FakeSessionPicker : ISessionPickerDialog
    {
        public SessionPick? Next { get; set; }

        public IReadOnlyList<Workout>? Offered { get; private set; }

        public Task<SessionPick?> ShowAsync(IReadOnlyList<Workout> workouts)
        {
            Offered = workouts;
            return Task.FromResult(Next);
        }
    }

    private sealed class FakeCatalogServiceWith(IReadOnlyList<Workout> workouts, params WorkoutProgram[] programs) : ICatalogService
    {
        private readonly WorkoutCatalog _catalog = new(1, workouts, programs);

        public Task<WorkoutCatalog> GetCatalogAsync() => Task.FromResult(_catalog);

        public void InvalidateCustomPrograms() { }

        public void Invalidate() { }

        public Task<WorkoutProgram?> GetProgramAsync(string programId, int? revision = null) =>
            Task.FromResult(programs.FirstOrDefault(p => p.Id == programId && (revision is null || p.Revision == revision)));

        public Task<Workout?> GetWorkoutAsync(string workoutId) => Task.FromResult(_catalog.WorkoutsById.GetValueOrDefault(workoutId));

        public IReadOnlyList<ScheduleDay> GetTrackDays(ProgramTrack track) => track.Days;
    }
}
