using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging;
using ShowUp.Core.Catalog;
using ShowUp.Core.CustomPrograms;
using ShowUp.Core.Entitlements;
using ShowUp.Core.Localization;
using ShowUp.Core.Messages;
using ShowUp.Core.Navigation;
using ShowUp.Core.Services;
using ShowUp.Core.Storage;
using ShowUp.Core.Tracking;
using ShowUp.Services.Dialogs;

namespace ShowUp.Core.ViewModels;

public sealed record DayPreview(string DayLabel, string Text, bool IsRest);

public sealed record WeekPreview(string Label, string PhaseName, IReadOnlyList<DayPreview> Days);

/// <summary>One program: pick a track and a start date, then start it (alongside the others with Pro).</summary>
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
    private readonly IDialogService _dialogs;
    private readonly TimeProvider _time;
    private readonly IJournalDialogs _journalDialogs;
    private readonly IEntitlementService _entitlements;
    private readonly ICustomProgramService _customPrograms;
    private readonly IMessenger _messenger;
    private SynchronizationContext? _context;
    private readonly ILogger<ProgramDetailViewModel> _logger;
    private IReadOnlyDictionary<string, Workout> _workouts = new Dictionary<string, Workout>();
    private bool _isStarting;
    private ActiveEnrollment? _enrollment;
    private IReadOnlyList<ActiveEnrollment> _all = [];
    private bool _listening;

    public ProgramDetailViewModel(
        IStringLocalizer localizer,
        ICatalogService catalog,
        IActiveEnrollmentService enrollments,
        IConfirmationDialogService confirmation,
        ISectionNavigator navigator,
        INavigationService navigation,
        IDialogService dialogs,
        TimeProvider time,
        IJournalDialogs journalDialogs,
        IEntitlementService entitlements,
        ICustomProgramService customPrograms,
        IMessenger messenger,
        ILogger<ProgramDetailViewModel> logger)
    {
        _localizer = localizer;
        _catalog = catalog;
        _enrollments = enrollments;
        _confirmation = confirmation;
        _navigator = navigator;
        _navigation = navigation;
        _dialogs = dialogs;
        _time = time;
        _journalDialogs = journalDialogs;
        _entitlements = entitlements;
        _customPrograms = customPrograms;
        _messenger = messenger;
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

    /// <summary>This is the program being tracked, so it has notes and results.</summary>
    [ObservableProperty]
    public partial bool IsActiveProgram { get; private set; }

    /// <summary>Free with another program running (starting this one replaces it), or this one is read-only after a lapse.</summary>
    [ObservableProperty]
    public partial bool ShowMultiProgramHint { get; private set; }

    [ObservableProperty]
    public partial string TrackSummary { get; private set; } = "";

    /// <summary>"Custom · v2" for the user's own programs.</summary>
    [ObservableProperty]
    public partial string VersionText { get; private set; } = "";

    /// <summary>A custom program and Pro: it can be edited, deleted and started.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowCustomLockedHint))]
    [NotifyCanExecuteChangedFor(nameof(EditCommand), nameof(DeleteCommand), nameof(StartCommand))]
    public partial bool CanEditCustom { get; private set; }

    public bool IsCustom => Program?.IsCustom == true;

    /// <summary>A custom program after a lapse: visible, but read-only until Pro is back.</summary>
    public bool ShowCustomLockedHint => IsCustom && !CanEditCustom;

    public string FamilyText => IsCustom ? _localizer["CustomProgramFamily"] : Program?.Family ?? "";

    public bool ShowContent => Program is not null;

    public bool ShowTrackSelector => Tracks.Count > 1;

    public bool IsCommunityVerified => Program?.Verification == VerificationLevel.Community;

    public string VerificationGlyph => IsCustom ? "\uE70F" : IsCommunityVerified ? "\uE716" : "\uE930";

    public string VerificationText => IsCustom ? VersionText : _localizer[IsCommunityVerified ? "VerificationCommunity" : "VerificationOfficial"];

    public string VerificationTooltip => _localizer[IsCustom ? "CustomProgramBadgeTooltip" : IsCommunityVerified ? "VerificationCommunityTooltip" : "VerificationOfficialTooltip"];

    public Uri ReportIssueUri => new($"{IssueUrl}?title={Uri.EscapeDataString($"Calendar issue: {Program?.Name}")}");

    /// <summary>Early enough to catch up on a program already under way.</summary>
    public DateTimeOffset MinStartDate => _time.GetLocalNow().AddDays(-MaxDaysBack);

    public DateTimeOffset MaxStartDate => _time.GetLocalNow().AddDays(MaxDaysAhead);

    public override void OnNavigatedTo(object? parameter)
    {
        base.OnNavigatedTo(parameter);
        if (!_listening)
        {
            _listening = true;
            _context = SynchronizationContext.Current;
            _entitlements.Changed += OnEntitlementsChanged;

            // A primary chosen after a lapse (or a program started elsewhere) changes the hint.
            _messenger.Register<ProgramDetailViewModel, TrackingChangedMessage>(this, static (r, _) => r.Post(() => r.RunInBackground(r.ReloadEnrollmentsAsync)));
        }

        if (parameter is string programId)
        {
            RunInBackground(() => LoadAsync(programId));
        }
    }

    public async Task LoadAsync(string programId)
    {
        IsLoading = true;
        IsNotFound = false;
        try
        {
            var program = await _catalog.GetProgramAsync(programId);
            if (program is null)
            {
                IsNotFound = true;
                return;
            }

            _workouts = (await _catalog.GetCatalogAsync()).WorkoutsById;
            VersionText = program.IsCustom
                ? _localizer["CustomProgramBadgeFormat", await _customPrograms.GetVersionNumberAsync(program.Id, program.Revision)]
                : "";
            _all = await _enrollments.GetAllAsync();
            _enrollment = _all.FirstOrDefault(e => e.Program.Id == program.Id);
            IsActiveProgram = _enrollment is not null;
            UpdateMultiProgramHint();
            Program = program;
            CanEditCustom = program.IsCustom && _customPrograms.CanEdit;
            PageTitle = program.Name;
            SelectedTrack = program.Tracks.FirstOrDefault();
            Tracks = program.Tracks;
            OnPropertyChanged(nameof(ShowContent));
            OnPropertyChanged(nameof(ShowTrackSelector));
            OnPropertyChanged(nameof(IsCommunityVerified));
            OnPropertyChanged(nameof(IsCustom));
            OnPropertyChanged(nameof(ShowCustomLockedHint));
            OnPropertyChanged(nameof(FamilyText));
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

    public override void OnNavigatedFrom()
    {
        base.OnNavigatedFrom();
        if (_listening)
        {
            _listening = false;
            _entitlements.Changed -= OnEntitlementsChanged;
            _messenger.UnregisterAll(this);
        }
    }

    /// <summary>Results of this program, without changing which program Calendar shows.</summary>
    [RelayCommand]
    private void OpenResults() => _navigation.Navigate<ProgramResultsViewModel>(_enrollment?.Id);

    [RelayCommand]
    private Task EditProgramNoteAsync() => _enrollment is { } enrollment ? _journalDialogs.EditProgramNoteAsync(enrollment.Id) : Task.CompletedTask;

    [RelayCommand]
    private void OpenPro() => _navigation.Navigate<ProViewModel>();

    private bool CanStart => Program is not null && SelectedTrack is not null && (!IsCustom || CanEditCustom);

    [RelayCommand(CanExecute = nameof(CanEditCustom))]
    private void Edit()
    {
        if (Program is { IsCustom: true } program)
        {
            _navigation.Navigate<ScheduleBuilderViewModel>(program.Id);
        }
    }

    /// <summary>Hides the program after a confirmation; running enrollments keep their pinned revision and history.</summary>
    [RelayCommand(CanExecute = nameof(CanEditCustom))]
    public async Task DeleteAsync()
    {
        if (Program is not { IsCustom: true } program)
        {
            return;
        }

        var answer = await _confirmation.ShowAsync(
            _localizer["CustomProgramDeleteTitle"],
            _localizer[IsActiveProgram ? "CustomProgramDeleteRunningMessageFormat" : "CustomProgramDeleteMessageFormat", program.Name]);
        if (answer != ConfirmationResult.Confirmed)
        {
            return;
        }

        try
        {
            await _customPrograms.DeleteAsync(program.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Deleting custom program {ProgramId} failed.", program.Id);
            CanEditCustom = _customPrograms.CanEdit;
            await _dialogs.ShowAsync(_localizer["CustomProgramDeleteFailedTitle"], _localizer["CustomProgramDeleteFailedMessage"]);
            return;
        }

        _navigation.GoBack();
    }

    [RelayCommand(CanExecute = nameof(CanStart))]
    public async Task StartAsync()
    {
        if (_isStarting || Program is not { } program || SelectedTrack is not { } track)
        {
            return;
        }

        _isStarting = true;
        try
        {
            // Pro runs programs side by side; restarting one that's running, or Free with one running, replaces it.
            var all = await _enrollments.GetAllAsync();
            var replaced = all.FirstOrDefault(e => e.Program.Id == program.Id)
                ?? (FeatureGate.MaxActivePrograms(_entitlements.IsPro) <= all.Count ? await _enrollments.GetPrimaryAsync() : null);
            if (replaced is not null && _enrollments.IsReadOnly(replaced))
            {
                await _dialogs.ShowAsync(_localizer["ProgramReadOnlyRestartTitle"], _localizer["ProgramReadOnlyRestartMessage"]);
                return;
            }

            if (replaced is not null)
            {
                var answer = await _confirmation.ShowAsync(
                    _localizer["ProgramReplaceTitle"],
                    _localizer["ProgramReplaceMessageFormat", replaced.Program.Name, program.Name]);
                if (answer != ConfirmationResult.Confirmed)
                {
                    return;
                }
            }

            try
            {
                var start = ClampedStartDate();
                if (replaced is null)
                {
                    await _enrollments.EnrollAsync(program, track, start);
                }
                else
                {
                    await _enrollments.ReplaceAsync(replaced.Id, EnrollmentEndReason.Abandoned, program, track, start);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Starting program {ProgramId} failed.", program.Id);
                await _dialogs.ShowAsync(_localizer["ProgramStartFailedTitle"], _localizer["ProgramStartFailedMessage"]);
                return;
            }

            _navigator.NavigateToSection(NavigationSection.Calendar);
        }
        finally
        {
            _isStarting = false;
        }
    }

    private DateOnly ClampedStartDate()
    {
        DateOnly picked = DateOnly.FromDateTime((StartDate ?? _time.GetLocalNow()).DateTime);
        DateOnly min = DateOnly.FromDateTime(MinStartDate.DateTime);
        DateOnly max = DateOnly.FromDateTime(MaxStartDate.DateTime);
        return picked < min ? min : picked > max ? max : picked;
    }

    private void OnEntitlementsChanged(object? sender, EventArgs e) => Post(() =>
    {
        UpdateMultiProgramHint();
        CanEditCustom = IsCustom && _customPrograms.CanEdit;
    });

    public async Task ReloadEnrollmentsAsync()
    {
        if (Program is not { } program)
        {
            return;
        }

        _all = await _enrollments.GetAllAsync();
        _enrollment = _all.FirstOrDefault(e => e.Program.Id == program.Id);
        IsActiveProgram = _enrollment is not null;
        UpdateMultiProgramHint();
    }

    private void Post(Action action)
    {
        if (_context is null || _context == SynchronizationContext.Current)
        {
            action();
        }
        else
        {
            _context.Post(_ => action(), null);
        }
    }

    private void UpdateMultiProgramHint() =>
        ShowMultiProgramHint = !_entitlements.IsPro
            && _all.Count > 0
            && (_enrollment is null || _enrollments.IsReadOnly(_enrollment));

    partial void OnProgramChanged(WorkoutProgram? value) => StartCommand.NotifyCanExecuteChanged();

    partial void OnSelectedTrackChanged(ProgramTrack? value)
    {
        StartCommand.NotifyCanExecuteChanged();
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
