using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using ShowUp.Core.Catalog;
using ShowUp.Core.Navigation;
using ShowUp.Core.Storage;
using ShowUp.Core.Tests.Tracking;
using ShowUp.Core.Tracking;
using ShowUp.Core.ViewModels;
using ShowUp.Services.Dialogs;

namespace ShowUp.Core.Tests.ViewModels;

[TestClass]
public sealed class ProgramDetailViewModelTests
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 21, 10, 0, 0, TimeSpan.Zero));
    private readonly RecordingEnrollmentService _enrollments = new();
    private readonly StubConfirmationDialogService _confirmation = new();
    private readonly RecordingSectionNavigator _navigator = new();
    private readonly RecordingNavigationService _navigation = new();

    public ProgramDetailViewModelTests() => _time.SetLocalTimeZone(TimeZoneInfo.Utc);

    [TestMethod]
    public async Task Detail_DefaultTrack_IsFirst()
    {
        var viewModel = await LoadAsync(TwoTracks());

        viewModel.SelectedTrack!.Id.Should().Be("first");
        viewModel.ShowTrackSelector.Should().BeTrue();
    }

    [TestMethod]
    public async Task Detail_SingleTrack_HidesTheSelector()
    {
        var viewModel = await LoadAsync(TestCatalog.Program("p", 1, "run"));

        viewModel.ShowTrackSelector.Should().BeFalse();
    }

    [TestMethod]
    public async Task Detail_StartDate_DefaultsToToday()
    {
        var viewModel = await LoadAsync(TestCatalog.Program("p", 1, "run"));

        viewModel.StartDate!.Value.Date.Should().Be(new DateTime(2026, 11, 21));
        viewModel.MinStartDate.Date.Should().Be(new DateTime(2026, 10, 22));
    }

    [TestMethod]
    public async Task Detail_ClearingStartDate_FallsBackToToday()
    {
        var viewModel = await LoadAsync(TestCatalog.Program("p", 1, "run"));

        viewModel.StartDate = null;

        viewModel.StartDate!.Value.Date.Should().Be(new DateTime(2026, 11, 21));
    }

    [TestMethod]
    public async Task Detail_WeekPreview_JoinsSessionsAndMarksOptional()
    {
        var viewModel = await LoadAsync(TestCatalog.Program("p", 1, "run+abs?", "rest", "run", "run", "run", "run", "run", "abs"));

        viewModel.WeekPreviews.Should().HaveCount(2);
        var first = viewModel.WeekPreviews[0].Days;
        first[0].Text.Should().Be("Run + ProgramOptionalFormat(Abs)");
        first[1].IsRest.Should().BeTrue();
        viewModel.WeekPreviews[1].Days.Should().ContainSingle().Which.Text.Should().Be("Abs");
    }

    [TestMethod]
    public async Task Detail_SelectingAnotherTrack_RebuildsThePreview()
    {
        var program = TwoTracks();
        var viewModel = await LoadAsync(program);

        viewModel.SelectedTrack = program.Tracks[1];

        viewModel.WeekPreviews.Should().ContainSingle().Which.Days.Should().HaveCount(2);
    }

    [TestMethod]
    public async Task Detail_CommunityProgram_OffersAPrefilledIssueLink()
    {
        var viewModel = await LoadAsync(TestCatalog.Program("p", 1, "run") with { Verification = VerificationLevel.Community, Name = "Insanity Unhinged" });

        viewModel.IsCommunityVerified.Should().BeTrue();
        viewModel.ReportIssueUri.AbsoluteUri.Should().Be("https://github.com/MartinZikmund/show-up/issues/new?title=Calendar%20issue%3A%20Insanity%20Unhinged");
    }

    [TestMethod]
    public async Task Start_NoActiveEnrollment_EnrollsAndGoesToCalendar()
    {
        var program = TwoTracks();
        var viewModel = await LoadAsync(program);
        viewModel.SelectedTrack = program.Tracks[1];
        viewModel.StartDate = new DateTimeOffset(2026, 11, 15, 0, 0, 0, TimeSpan.Zero);

        await viewModel.StartAsync();

        _confirmation.Asked.Should().BeEmpty();
        _enrollments.Enrolled.Should().Be((program, program.Tracks[1], new DateOnly(2026, 11, 15)));
        _navigator.Sections.Should().Equal(NavigationSection.Calendar);
    }

    [TestMethod]
    public async Task Start_WithActiveEnrollment_EndsPreviousAfterConfirm()
    {
        _enrollments.Active = ActiveOne();
        var viewModel = await LoadAsync(TestCatalog.Program("new", 1, "run"));

        await viewModel.StartAsync();

        _confirmation.Asked.Should().ContainSingle().Which.Text.Should().Contain("Program old r1").And.Contain("Program new r1");
        _enrollments.Calls.Should().Equal($"End:{EnrollmentEndReason.Abandoned}", "Enroll");
        _navigator.Sections.Should().Equal(NavigationSection.Calendar);
    }

    [TestMethod]
    public async Task Start_Declined_KeepsPrevious()
    {
        _enrollments.Active = ActiveOne();
        _confirmation.Result = ConfirmationResult.Denied;
        var viewModel = await LoadAsync(TestCatalog.Program("new", 1, "run"));

        await viewModel.StartAsync();

        _enrollments.Calls.Should().BeEmpty();
        _enrollments.Active.Should().NotBeNull();
        _navigator.Sections.Should().BeEmpty();
    }

    [TestMethod]
    public async Task GoBack_ReturnsToThePreviousPage()
    {
        var viewModel = await LoadAsync(TestCatalog.Program("p", 1, "run"));

        viewModel.GoBackCommand.Execute(null);

        _navigation.BackCount.Should().Be(1);
    }

    private static WorkoutProgram TwoTracks()
    {
        var program = TestCatalog.Program("p", 1, "run", "run", "run");
        var second = new ProgramTrack("second", "Second", [new ProgramPhase("Ph", [new ScheduleWeek([new ScheduleDay([new ScheduledSession("abs")]), new ScheduleDay([new ScheduledSession("abs")])])])]);
        return program with { Tracks = [program.Tracks[0] with { Id = "first" }, second] };
    }

    private static ActiveEnrollment ActiveOne()
    {
        var old = TestCatalog.Program("old", 1, "run");
        return new ActiveEnrollment(new EnrollmentEntity(), old, old.Tracks[0], new EnrollmentCalendar(new DateOnly(2026, 11, 1), 1, []));
    }

    private async Task<ProgramDetailViewModel> LoadAsync(WorkoutProgram program)
    {
        ProgramDetailViewModel viewModel = new(
            new PassThroughLocalizer(),
            new FakeCatalogService(program),
            _enrollments,
            _confirmation,
            _navigator,
            _navigation,
            _time,
            NullLogger<ProgramDetailViewModel>.Instance);
        await viewModel.LoadAsync(program.Id);
        return viewModel;
    }
}
