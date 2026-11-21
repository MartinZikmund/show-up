using Microsoft.Extensions.Logging;
using ShowUp.Core.Catalog;
using ShowUp.Core.Localization;
using ShowUp.Core.Navigation;
using ShowUp.Core.Services;
using ShowUp.Core.Storage;
using ShowUp.Core.Tracking;
using ShowUp.Services.Dialogs;

namespace ShowUp.Core.ViewModels;

public sealed record DayPreview(string DayLabel, string Text, bool IsRest);

public sealed record WeekPreview(string Label, string PhaseName, IReadOnlyList<DayPreview> Days);

/// <summary>One program: pick a track and a start date, then start it.</summary>
public partial class ProgramDetailViewModel : ViewModelBase
{
    private const string IssueUrl = "https://github.com/MartinZikmund/show-up/issues/new";
    private const int MaxDaysBack = 30;
    private const int MaxDaysAhead = 365;

    private readonly IStringLocalizer _localizer;
    private readonly ICatalogService _catalog;
    private readonly IActiveEnrollmentService _enrollments;
    private readonly IConfirmationDialogService _confirmation;
    private readonly ISectionNavigator _navigator;
    private readonly INavigationService _navigation;
    private readonly TimeProvider _time;
    private readonly ILogger<ProgramDetailViewModel> _logger;
    private IReadOnlyDictionary<string, Workout> _workouts = new Dictionary<string, Workout>();
    private bool _isStarting;

    public ProgramDetailViewModel(
        IStringLocalizer localizer,
        ICatalogService catalog,
        IActiveEnrollmentService enrollments,
        IConfirmationDialogService confirmation,
        ISectionNavigator navigator,
        INavigationService navigation,
        TimeProvider time,
        ILogger<ProgramDetailViewModel> logger)
    {
        _localizer = localizer;
        _catalog = catalog;
        _enrollments = enrollments;
        _confirmation = confirmation;
        _navigator = navigator;
        _navigation = navigation;
        _time = time;
        _logger = logger;
        StartDate = _time.GetLocalNow();
    }

    [ObservableProperty]
    public partial WorkoutProgram? Program { get; private set; }

    [ObservableProperty]
    public partial IReadOnlyList<ProgramTrack> Tracks { get; private set; } = [];

    [ObservableProperty]
    public partial ProgramTrack? SelectedTrack { get; set; }

    [ObservableProperty]
    public partial IReadOnlyList<WeekPreview> WeekPreviews { get; private set; } = [];

    /// <summary>The date picker's value; clearing it falls back to today.</summary>
    [ObservableProperty]
    public partial DateTimeOffset? StartDate { get; set; }

    [ObservableProperty]
    public partial bool IsNotFound { get; private set; }

    [ObservableProperty]
    public partial string TrackSummary { get; private set; } = "";

    public bool ShowTrackSelector => Tracks.Count > 1;

    public bool IsCommunityVerified => Program?.Verification == VerificationLevel.Community;

    public string VerificationGlyph => IsCommunityVerified ? "\uE716" : "\uE930";

    public string VerificationText => _localizer[IsCommunityVerified ? "VerificationCommunity" : "VerificationOfficial"];

    public string VerificationTooltip => _localizer[IsCommunityVerified ? "VerificationCommunityTooltip" : "VerificationOfficialTooltip"];

    public Uri ReportIssueUri => new($"{IssueUrl}?title={Uri.EscapeDataString($"Calendar issue: {Program?.Name}")}");

    /// <summary>Early enough to catch up on a program already under way.</summary>
    public DateTimeOffset MinStartDate => _time.GetLocalNow().AddDays(-MaxDaysBack);

    public DateTimeOffset MaxStartDate => _time.GetLocalNow().AddDays(MaxDaysAhead);

    public override void OnNavigatedTo(object? parameter)
    {
        base.OnNavigatedTo(parameter);
        if (parameter is string programId)
        {
            RunInBackground(() => LoadAsync(programId));
        }
    }

    public async Task LoadAsync(string programId)
    {
        IsLoading = true;
        try
        {
            var program = await _catalog.GetProgramAsync(programId);
            if (program is null)
            {
                IsNotFound = true;
                return;
            }

            _workouts = (await _catalog.GetCatalogAsync()).WorkoutsById;
            Program = program;
            PageTitle = program.Name;
            Tracks = program.Tracks;
            SelectedTrack = program.Tracks.FirstOrDefault();
            OnPropertyChanged(nameof(ShowTrackSelector));
            OnPropertyChanged(nameof(IsCommunityVerified));
            OnPropertyChanged(nameof(VerificationGlyph));
            OnPropertyChanged(nameof(VerificationText));
            OnPropertyChanged(nameof(VerificationTooltip));
            OnPropertyChanged(nameof(ReportIssueUri));
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void GoBack() => _navigation.GoBack();

    [RelayCommand]
    public async Task StartAsync()
    {
        if (_isStarting || Program is not { } program || SelectedTrack is not { } track)
        {
            return;
        }

        _isStarting = true;
        try
        {
            if (await _enrollments.GetAsync() is { } active)
            {
                var answer = await _confirmation.ShowAsync(
                    _localizer["ProgramReplaceTitle"],
                    _localizer["ProgramReplaceMessageFormat", active.Program.Name, program.Name]);
                if (answer != ConfirmationResult.Confirmed)
                {
                    return;
                }

                await _enrollments.EndAsync(EnrollmentEndReason.Abandoned);
            }

            DateTimeOffset picked = StartDate ?? _time.GetLocalNow();
            await _enrollments.EnrollAsync(program, track, DateOnly.FromDateTime(picked.DateTime));
            _navigator.NavigateToSection(NavigationSection.Calendar);
        }
        finally
        {
            _isStarting = false;
        }
    }

    partial void OnSelectedTrackChanged(ProgramTrack? value)
    {
        WeekPreviews = value is null ? [] : BuildWeeks(value);
        TrackSummary = value is null
            ? ""
            : _localizer["ProgramCardSummaryFormat", _localizer.Format("ProgramDays", _catalog.GetTrackDays(value).Count), _localizer.Format("ProgramWeeks", WeekPreviews.Count)].Value;
    }

    partial void OnStartDateChanged(DateTimeOffset? value)
    {
        if (value is null)
        {
            StartDate = _time.GetLocalNow();
        }
    }

    private List<WeekPreview> BuildWeeks(ProgramTrack track)
    {
        List<WeekPreview> weeks = [];
        int dayNumber = 0;
        int weekNumber = 0;
        foreach (ProgramPhase phase in track.Phases)
        {
            foreach (ScheduleWeek week in phase.Weeks)
            {
                weekNumber++;
                List<DayPreview> days = [];
                foreach (ScheduleDay day in week.Days)
                {
                    dayNumber++;
                    days.Add(new DayPreview(
                        _localizer["CalendarDayNumberFormat", dayNumber].Value,
                        string.Join(" + ", day.Sessions.Select(SessionName)),
                        day.IsRestDay(_workouts)));
                }

                weeks.Add(new WeekPreview(
                    _localizer["CalendarWeekFormat", weekNumber].Value,
                    track.Phases.Count > 1 ? phase.Name : "",
                    days));
            }
        }

        return weeks;
    }

    private string SessionName(ScheduledSession session)
    {
        string name = _workouts.TryGetValue(session.WorkoutId, out var workout) ? workout.Name : session.WorkoutId;
        return session.Optional ? _localizer["ProgramOptionalFormat", name].Value : name;
    }

    private async void RunInBackground(Func<Task> work)
    {
        try
        {
            await work();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Loading the program failed.");
        }
    }
}
