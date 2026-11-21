using AwesomeAssertions;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using ShowUp.Core.Catalog;
using ShowUp.Core.Marking;
using ShowUp.Core.Tests.Tracking;
using ShowUp.Core.Tracking;
using ShowUp.Core.ViewModels;
using ShowUp.Services.Dialogs;
using ShowUp.Services.Settings;

namespace ShowUp.Core.Tests.ViewModels;

[TestClass]
public sealed class TodayViewModelTests
{
    private static readonly DateOnly _today = new(2026, 11, 21);

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 21, 0, 0, 0, TimeSpan.Zero));
    private readonly FakeTrackingRepository _repository;
    private readonly FakeAppPreferences _preferences = new() { CrossOffStyle = CrossOffStyle.AutoMarker, CrossMomentMode = CrossMomentMode.Never };
    private readonly FakeCrossMoment _crossMoment = new();
    private readonly FakeConfirmationDialogService _confirmation = new();
    private readonly FakeBonusWorkoutDialog _bonusDialog = new();

    public TodayViewModelTests()
    {
        _time.SetLocalTimeZone(TimeZoneInfo.Utc);
        _repository = new(_time);
    }

    [TestMethod]
    public async Task Load_RestDay_ShowsRestState()
    {
        var viewModel = await LoadAsync(_today, "rest");

        viewModel.IsRestDay.Should().BeTrue();
        viewModel.DayNumber.Should().Be(1);
        viewModel.Sessions.Should().ContainSingle();
    }

    [TestMethod]
    public async Task Load_TrainingDay_ListsSessionsWithDetails()
    {
        var viewModel = await LoadAsync(_today.AddDays(-2), "run", "run", "run+abs");

        viewModel.IsRestDay.Should().BeFalse();
        viewModel.DayNumber.Should().Be(3);
        viewModel.Sessions.Select(s => s.Name).Should().Equal("Run", "Abs");
        viewModel.Sessions[0].DurationMinutes.Should().Be(30);
        viewModel.Sessions[0].Kind.Should().Be(WorkoutKind.Cardio);
    }

    [TestMethod]
    public async Task Load_ProgramNotStarted_IsOffSchedule()
    {
        var viewModel = await LoadAsync(_today.AddDays(3), "run");

        viewModel.HasEnrollment.Should().BeTrue();
        viewModel.IsOffSchedule.Should().BeTrue();
        viewModel.Sessions.Should().BeEmpty();
    }

    [TestMethod]
    public async Task Load_NoEnrollment_ShowsInvitation()
    {
        var viewModel = CreateViewModel(new FakeCatalogService());

        await viewModel.LoadAsync();

        viewModel.ShowNoEnrollment.Should().BeTrue();
        viewModel.HasEnrollment.Should().BeFalse();
    }

    [TestMethod]
    [DataRow(6, "GreetingMorning")]
    [DataRow(14, "GreetingAfternoon")]
    [DataRow(20, "GreetingEvening")]
    [DataRow(2, "GreetingNight")]
    public async Task Load_HourOfDay_PicksGreeting(int hour, string expected)
    {
        _time.SetUtcNow(new DateTimeOffset(2026, 11, 21, hour, 0, 0, TimeSpan.Zero));
        var viewModel = CreateViewModel(new FakeCatalogService());

        await viewModel.LoadAsync();

        viewModel.Greeting.Should().Be(expected);
    }

    [TestMethod]
    public async Task Cross_FromToday_CrossesCorrectSession()
    {
        var viewModel = await LoadAsync(_today.AddDays(-1), "run", "run+abs");

        await viewModel.Sessions[1].CrossCommand.ExecuteAsync(null);

        var cross = _repository.Crosses.Should().ContainSingle().Subject;
        (cross.Day, cross.Session, cross.Date).Should().Be((1, 1, _today));
        viewModel.Sessions[1].IsDone.Should().BeTrue();
        viewModel.Sessions[0].IsDone.Should().BeFalse();
        viewModel.MarkStrokes.Should().Be(1);
    }

    [TestMethod]
    public async Task Cross_LastSession_CompletesDayAndStreak()
    {
        var viewModel = await LoadAsync(_today, "run");

        await viewModel.Sessions[0].CrossCommand.ExecuteAsync(null);

        viewModel.MarkStrokes.Should().Be(2);
        viewModel.CurrentStreak.Should().Be(1);
    }

    [TestMethod]
    public async Task Cross_TodayWithMomentAlways_ShowsCrossMoment()
    {
        _preferences.CrossMomentMode = CrossMomentMode.Always;
        var viewModel = await LoadAsync(_today, "run");

        await viewModel.Sessions[0].CrossCommand.ExecuteAsync(null);

        _crossMoment.Shown.Should().ContainSingle().Which.Title.Should().Be("Run");
    }

    [TestMethod]
    public async Task Cross_DrawnCross_PersistsInk()
    {
        _preferences.CrossOffStyle = CrossOffStyle.DrawYourself;
        _crossMoment.DrawResult = CrossMomentResult.Drawn("[[0,0,1,1]]");
        var viewModel = await LoadAsync(_today, "run");

        await viewModel.Sessions[0].CrossCommand.ExecuteAsync(null);

        _repository.Crosses.Should().ContainSingle().Which.Ink.Should().Be("[[0,0,1,1]]");
        viewModel.Inks.Should().ContainSingle();
    }

    [TestMethod]
    public async Task Cross_DrawCancelled_DoesNotWrite()
    {
        _preferences.CrossOffStyle = CrossOffStyle.DrawYourself;
        _crossMoment.DrawResult = CrossMomentResult.Cancelled;
        var viewModel = await LoadAsync(_today, "run");

        await viewModel.Sessions[0].CrossCommand.ExecuteAsync(null);

        _repository.Crosses.Should().BeEmpty();
        viewModel.Sessions[0].IsDone.Should().BeFalse();
    }

    [TestMethod]
    public async Task Bonus_NoEnrollment_StillLogs()
    {
        var viewModel = CreateViewModel(new FakeCatalogService());
        await viewModel.LoadAsync();
        _bonusDialog.Title = "Ab Attack: 10";

        await viewModel.LogBonusCommand.ExecuteAsync(null);

        viewModel.Bonuses.Should().ContainSingle().Which.Title.Should().Be("Ab Attack: 10");
    }

    [TestMethod]
    public async Task Bonus_Cancelled_DoesNotLog()
    {
        var viewModel = await LoadAsync(_today, "run");
        _bonusDialog.Title = null;

        await viewModel.LogBonusCommand.ExecuteAsync(null);

        viewModel.Bonuses.Should().BeEmpty();
    }

    [TestMethod]
    public async Task RemoveBonus_Confirmed_RemovesIt()
    {
        var viewModel = await LoadAsync(_today, "run");
        _bonusDialog.Title = "Yoga";
        await viewModel.LogBonusCommand.ExecuteAsync(null);

        await viewModel.RemoveBonusCommand.ExecuteAsync(viewModel.Bonuses[0]);

        viewModel.Bonuses.Should().BeEmpty();
    }

    [TestMethod]
    public async Task RemoveBonus_Declined_KeepsIt()
    {
        var viewModel = await LoadAsync(_today, "run");
        _bonusDialog.Title = "Yoga";
        await viewModel.LogBonusCommand.ExecuteAsync(null);
        _confirmation.Result = ConfirmationResult.Denied;

        await viewModel.RemoveBonusCommand.ExecuteAsync(viewModel.Bonuses[0]);

        viewModel.Bonuses.Should().ContainSingle();
    }

    private async Task<TodayViewModel> LoadAsync(DateOnly start, params string[] days)
    {
        var program = TestCatalog.Program("p", 1, days);
        var viewModel = CreateViewModel(new FakeCatalogService(program));
        await _repository.EnrollAsync("p", 1, "t", start);
        await viewModel.LoadAsync();
        return viewModel;
    }

    private TodayViewModel CreateViewModel(FakeCatalogService catalog)
    {
        WeakReferenceMessenger messenger = new();
        ActiveEnrollmentService service = new(_repository, catalog, messenger, NullLogger<ActiveEnrollmentService>.Instance);
        return new(
            new FakeStringLocalizer(),
            service,
            catalog,
            _time,
            messenger,
            new CrossingCoordinator(_preferences, _crossMoment, new FakeMarkingFeedback(), service),
            _confirmation,
            _bonusDialog,
            new FakeSectionNavigator(),
            NullLogger<TodayViewModel>.Instance);
    }
}
