using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging;
using ShowUp.Core.Catalog;
using ShowUp.Core.Marking;
using ShowUp.Core.Messages;
using ShowUp.Core.Navigation;
using ShowUp.Core.Services;
using ShowUp.Core.Storage;
using ShowUp.Core.Tracking;
using ShowUp.Services.Dialogs;
using ShowUp.Services.Settings;

namespace ShowUp.Core.ViewModels;

/// <summary>The paper calendar of the selected enrollment (a switcher picks among several): program weeks as rows, tap a day to cross off its next session.</summary>
public partial class ProgramCalendarViewModel : ViewModelBase
{
    private readonly IStringLocalizer _localizer;
    private readonly IActiveEnrollmentService _enrollments;
    private readonly ICatalogService _catalog;
    private readonly TimeProvider _time;
    private readonly IMessenger _messenger;
    private readonly ICrossingCoordinator _crossing;
    private readonly IConfirmationDialogService _confirmation;
    private readonly IDialogService _dialogs;
    private readonly ICalendarPrompts _prompts;
    private readonly IBonusWorkoutDialog _bonusDialog;
    private readonly ISectionNavigator _navigator;
    private readonly IJournalDialogs _journalDialogs;
    private readonly INavigationService _navigation;
    private readonly ILogger<ProgramCalendarViewModel> _logger;

    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private readonly HashSet<int> _inFlight = [];
    private List<CalendarDayViewModel> _days = [];
    private IReadOnlyList<DayStatus> _statuses = [];
    private IReadOnlyList<CompletedSession> _completions = [];
    private IReadOnlyList<ScheduleDay> _trackDays = [];
    private IReadOnlyDictionary<string, Workout> _workouts = new Dictionary<string, Workout>();
    private ActiveEnrollment? _active;
    private DateOnly _loadedToday;
    private bool _syncingSelection;
    private readonly LiveRefresher _live;

    public ProgramCalendarViewModel(
        IStringLocalizer localizer,
        IActiveEnrollmentService enrollments,
        ICatalogService catalog,
        TimeProvider time,
        IMessenger messenger,
        ICrossingCoordinator crossing,
        IConfirmationDialogService confirmation,
        IDialogService dialogs,
        ICalendarPrompts prompts,
        IBonusWorkoutDialog bonusDialog,
        ISectionNavigator navigator,
        IJournalDialogs journalDialogs,
        INavigationService navigation,
        ILogger<ProgramCalendarViewModel> logger)
    {
        _localizer = localizer;
        _enrollments = enrollments;
        _catalog = catalog;
        _time = time;
        _messenger = messenger;
        _crossing = crossing;
        _confirmation = confirmation;
        _dialogs = dialogs;
        _prompts = prompts;
        _bonusDialog = bonusDialog;
        _navigator = navigator;
        _journalDialogs = journalDialogs;
        _navigation = navigation;
        _logger = logger;
        _live = new(time, logger, "Refreshing the program calendar failed.", RefreshAsync, now => now.Date.AddDays(1) - now.DateTime);
        PageTitle = localizer["Calendar"];
    }

    public ObservableCollection<CalendarWeekViewModel> Weeks { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditable))]
    public partial bool HasEnrollment { get; private set; }

    /// <summary>Loaded and there's no enrollment (not shown while the first load runs).</summary>
    [ObservableProperty]
    public partial bool ShowEmptyState { get; private set; }

    [ObservableProperty]
    public partial string ProgramName { get; private set; } = "";

    /// <summary>Every active program, for the switcher.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowProgramSwitcher), nameof(ShowProgramTitle))]
    public partial IReadOnlyList<ProgramOption> Programs { get; private set; } = [];

    [ObservableProperty]
    public partial ProgramOption? SelectedProgram { get; set; }

    public bool ShowProgramSwitcher => Programs.Count > 1;

    /// <summary>With several programs the switcher is the title.</summary>
    public bool ShowProgramTitle => !ShowProgramSwitcher;

    /// <summary>Pro lapsed and this isn't the primary program: shown, not crossed.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditable))]
    public partial bool IsReadOnly { get; private set; }

    public bool IsEditable => HasEnrollment && !IsReadOnly;

    [ObservableProperty]
    public partial int CurrentStreak { get; private set; }

    [ObservableProperty]
    public partial int LongestStreak { get; private set; }

    [ObservableProperty]
    public partial string StreakText { get; private set; } = "";

    [ObservableProperty]
    public partial string LongestStreakText { get; private set; } = "";

    private DateOnly Today => DateOnly.FromDateTime(_time.GetLocalNow().DateTime);

    public override void ViewLoaded()
    {
        base.ViewLoaded();
        _live.RunInBackground(LoadAsync);
    }

    public override void ViewUnloaded()
    {
        base.ViewUnloaded();
        Unload();
    }

    /// <summary>Starts listening for tracking changes and midnight, then loads.</summary>
    public async Task LoadAsync()
    {
        if (_live.Activate())
        {
            _messenger.Register<ProgramCalendarViewModel, TrackingChangedMessage>(this, static (r, _) => r._live.OnTrackingChanged());
            _messenger.Register<ProgramCalendarViewModel, DataChangedMessage>(this, static (r, _) => r._live.OnDataChanged());
        }

        await RefreshAsync();
    }

    public void Unload()
    {
        _live.Deactivate();
        _messenger.UnregisterAll(this);
    }

    /// <summary>For window activation: the app may have slept through midnight.</summary>
    public Task RefreshIfDateChangedAsync() => Today != _loadedToday ? RefreshAsync() : Task.CompletedTask;

    public async Task RefreshAsync()
    {
        await _refreshGate.WaitAsync();
        try
        {
            var today = Today;
            _loadedToday = today;
            _live.ScheduleNextChange();

            var all = await _enrollments.GetAllAsync();
            var active = await _enrollments.GetSelectedAsync();
            if (active is null)
            {
                ShowNoEnrollment();
                return;
            }

            _workouts = (await _catalog.GetCatalogAsync()).WorkoutsById;
            var completions = await _enrollments.GetCompletionsAsync(active.Id);
            var bonuses = await _enrollments.GetBonusWorkoutsAsync(active.Id);
            var statuses = ProgressCalculator.Build(
                active.Track,
                _workouts,
                active.Calendar,
                completions.Select(c => (c.DayIndex, c.SessionIndex)).ToHashSet());

            if (_active?.Enrollment.Id != active.Enrollment.Id || _days.Count != statuses.Count)
            {
                _trackDays = _catalog.GetTrackDays(active.Track);
                RebuildWeeks(active.Track, statuses.Count);
            }

            var completionsByDay = completions.ToLookup(c => c.DayIndex);
            var bonusesByDate = bonuses.GroupBy(b => b.Date).ToDictionary(g => g.Key, g => g.Count());
            foreach (var status in statuses)
            {
                _days[status.DayIndex].Update(
                    status,
                    [.. completionsByDay[status.DayIndex]],
                    [.. _trackDays[status.DayIndex].Sessions.Select(s => WorkoutName(s.WorkoutId))],
                    bonusesByDate.GetValueOrDefault(status.Date),
                    today);
            }

            _active = active;
            _statuses = statuses;
            _completions = completions;
            ProgramName = active.Program.Name;
            SyncPrograms(all, active);
            IsReadOnly = _enrollments.IsReadOnly(active);
            CurrentStreak = StreakCalculator.Current(statuses, today);
            LongestStreak = StreakCalculator.Longest(statuses, today);
            StreakText = _localizer["CrossMomentStreakFormat", CurrentStreak].Value;
            LongestStreakText = _localizer["CalendarLongestStreakFormat", LongestStreak].Value;
            HasEnrollment = true;
            ShowEmptyState = false;
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    /// <summary>Crosses the day's lowest undone session (optional ones too): draw-it-yourself or the auto marker, per the user's style.</summary>
    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task CrossNextAsync(CalendarDayViewModel? day)
    {
        if (day is null || _active is null || day.DayIndex >= _statuses.Count)
        {
            return;
        }

        if (day.IsFuture || IsReadOnly)
        {
            day.RequestShake();
            return;
        }

        // A complete day can still have an optional session left to cross.
        if (!day.CanCross || !_inFlight.Add(day.DayIndex))
        {
            return;
        }

        try
        {
            var status = _statuses[day.DayIndex];
            var session = Enumerable.Range(0, status.SessionCount).FirstOrDefault(i => !status.CompletedSessions.Contains(i), -1);
            if (session < 0)
            {
                return;
            }

            var dayCompletions = _completions.Where(c => c.DayIndex == day.DayIndex).ToList();
            await _crossing.CrossAsync(
                new CrossTarget(_active.Id, day.DayIndex, session, SessionName(day.DayIndex, session), day.IsToday, day.Seed, status, _statuses, dayCompletions, Today),
                new CrossHooks(_live.WriteAsync, RefreshAsync, animate => day.AnimateMarks = animate));
        }
        catch (InvalidOperationException)
        {
            // The day turned future-locked or the enrollment ended under us; show what's true now.
            await RefreshAsync();
        }
        finally
        {
            day.AnimateMarks = true;
            _inFlight.Remove(day.DayIndex);
        }
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task UncrossLastAsync(CalendarDayViewModel? day)
    {
        if (day is null || _active is not { } active || IsReadOnly || day.DayIndex >= _statuses.Count
            || _statuses[day.DayIndex].CompletedSessions.Count == 0 || !_inFlight.Add(day.DayIndex))
        {
            return;
        }

        try
        {
            var session = _statuses[day.DayIndex].CompletedSessions.Max();
            var confirmed = await _confirmation.ShowAsync(
                _localizer["UncrossConfirmTitle"],
                _localizer["UncrossConfirmFormat", day.DayNumber, SessionName(day.DayIndex, session)]);
            if (confirmed != ConfirmationResult.Confirmed)
            {
                return;
            }

            await _live.WriteAsync(() => _enrollments.UncrossAsync(active.Id, day.DayIndex, session));
            await RefreshAsync();
        }
        catch (InvalidOperationException)
        {
            await RefreshAsync();
        }
        finally
        {
            _inFlight.Remove(day.DayIndex);
        }
    }

    [RelayCommand]
    private async Task LogBonusAsync(CalendarDayViewModel? day)
    {
        if (day is null || day.IsFuture || await _bonusDialog.ShowAsync(day.Date) is not { } entry)
        {
            return;
        }

        BonusWorkoutEntity? logged = null;
        try
        {
            await _live.WriteAsync(async () => logged = await _enrollments.LogBonusAsync(entry.WorkoutId, entry.Title, entry.Date, entry.DurationMinutes));
        }
        catch (InvalidOperationException)
        {
            // The enrollment ended under us; the refresh shows what's true now.
        }

        await RefreshAsync();
        if (logged?.WorkoutId is { } id && _workouts.GetValueOrDefault(id) is { HasMetrics: true })
        {
            await _journalDialogs.LogBonusResultsAsync(logged);
        }
    }

    /// <summary>A crossed day's sheet: sessions, notes, results, bonus workouts and uncross.</summary>
    [RelayCommand]
    private async Task OpenDayAsync(CalendarDayViewModel? day)
    {
        if (day is null || _active is null)
        {
            return;
        }

        await _journalDialogs.ShowDayAsync(_active.Id, day.DayIndex);
        await RefreshAsync();
    }

    [RelayCommand]
    private Task EditProgramNoteAsync() => _active is null ? Task.CompletedTask : _journalDialogs.EditProgramNoteAsync(_active.Id);

    [RelayCommand]
    private void OpenPro() => _navigation.Navigate<ProViewModel>();

    /// <summary>Shows another active program; the choice is remembered (and synced).</summary>
    public async Task SelectProgramAsync(ProgramOption? option)
    {
        if (option is null || option.Id == _active?.Id)
        {
            return;
        }

        try
        {
            await _live.WriteAsync(() => _enrollments.SelectAsync(option.Id));
        }
        catch (InvalidOperationException)
        {
            // It ended under us; the refresh shows what's true now.
        }

        await RefreshAsync();
    }

    partial void OnSelectedProgramChanged(ProgramOption? value)
    {
        if (!_syncingSelection)
        {
            _live.RunInBackground(() => SelectProgramAsync(value));
        }
    }

    [RelayCommand]
    private void OpenResults()
    {
        if (_active is not null)
        {
            _navigation.Navigate<ProgramResultsViewModel>();
        }
    }

    /// <summary>Pushes the rest of the program back from today; crossed days stay put.</summary>
    [RelayCommand]
    private async Task ShiftScheduleAsync()
    {
        if (_active is not { } active || IsReadOnly || await _prompts.AskShiftDaysAsync() is not { } days)
        {
            return;
        }

        try
        {
            await _live.WriteAsync(() => _enrollments.ShiftAsync(active.Id, Today, days));
        }
        catch (InvalidOperationException)
        {
            // Nothing left to shift: every remaining day is crossed, or the program is over.
            await _dialogs.ShowAsync(_localizer["ShiftFailedTitle"], _localizer["ShiftFailedMessage"]);
        }

        await RefreshAsync();
    }

    [RelayCommand]
    private void BrowsePrograms() => _navigator.NavigateToSection(NavigationSection.Programs);

    private void ShowNoEnrollment()
    {
        _active = null;
        _days = [];
        _statuses = [];
        _completions = [];
        Weeks.Clear();
        ProgramName = "";
        _syncingSelection = true;
        Programs = [];
        SelectedProgram = null;
        _syncingSelection = false;
        IsReadOnly = false;
        CurrentStreak = 0;
        LongestStreak = 0;
        StreakText = "";
        LongestStreakText = "";
        HasEnrollment = false;
        ShowEmptyState = true;
    }

    private void SyncPrograms(IReadOnlyList<ActiveEnrollment> all, ActiveEnrollment selected)
    {
        _syncingSelection = true;
        try
        {
            List<ProgramOption> options = [.. all.Select(e => new ProgramOption(e.Id, e.Program.Name))];
            if (!Programs.SequenceEqual(options))
            {
                Programs = options;
            }

            SelectedProgram = Programs.FirstOrDefault(o => o.Id == selected.Id);
        }
        finally
        {
            _syncingSelection = false;
        }
    }

    private void RebuildWeeks(ProgramTrack track, int dayCount)
    {
        _days = [.. Enumerable.Range(0, dayCount).Select(i => new CalendarDayViewModel(i, _localizer))];
        Weeks.Clear();

        string? previousPhase = null;
        foreach (var week in _days.GroupBy(d => track.Locate(d.DayIndex) is var l ? (l.PhaseName, l.WeekNumber) : default))
        {
            var (phase, number) = week.Key;
            Weeks.Add(new CalendarWeekViewModel(
                phase,
                number,
                phase != previousPhase,
                _localizer["CalendarWeekFormat", number].Value,
                [.. week]));
            previousPhase = phase;
        }
    }

    private string SessionName(int dayIndex, int session) => WorkoutName(_trackDays[dayIndex].Sessions[session].WorkoutId);

    private string WorkoutName(string workoutId) => _workouts.TryGetValue(workoutId, out var workout) ? workout.Name : workoutId;
}
