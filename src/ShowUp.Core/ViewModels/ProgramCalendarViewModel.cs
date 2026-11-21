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

/// <summary>The paper calendar of the active enrollment: program weeks as rows, tap a day to cross off its next session.</summary>
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
    private SynchronizationContext? _context;
    private ITimer? _midnightTimer;
    private bool _isActive;

    // While this view model writes, it refreshes itself; the echoed TrackingChangedMessage is skipped.
    private int _ownWrites;

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
        PageTitle = localizer["Calendar"];
    }

    public ObservableCollection<CalendarWeekViewModel> Weeks { get; } = [];

    [ObservableProperty]
    public partial bool HasEnrollment { get; private set; }

    /// <summary>Loaded and there's no enrollment (not shown while the first load runs).</summary>
    [ObservableProperty]
    public partial bool ShowEmptyState { get; private set; }

    [ObservableProperty]
    public partial string ProgramName { get; private set; } = "";

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
        RunInBackground(LoadAsync);
    }

    public override void ViewUnloaded()
    {
        base.ViewUnloaded();
        Unload();
    }

    /// <summary>Starts listening for tracking changes and midnight, then loads.</summary>
    public async Task LoadAsync()
    {
        if (!_isActive)
        {
            _isActive = true;
            _context = SynchronizationContext.Current;
            _messenger.Register<ProgramCalendarViewModel, TrackingChangedMessage>(this, static (r, _) => r.OnTrackingChanged());
        }

        await RefreshAsync();
    }

    public void Unload()
    {
        _isActive = false;
        _messenger.UnregisterAll(this);
        _midnightTimer?.Dispose();
        _midnightTimer = null;
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
            ScheduleMidnightRefresh();

            var active = await _enrollments.GetAsync();
            if (active is null)
            {
                ShowNoEnrollment();
                return;
            }

            _workouts = (await _catalog.GetCatalogAsync()).WorkoutsById;
            var completions = await _enrollments.GetCompletionsAsync();
            var bonuses = await _enrollments.GetBonusWorkoutsAsync();
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

        if (day.IsFuture)
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
                new CrossTarget(day.DayIndex, session, SessionName(day.DayIndex, session), day.IsToday, day.Seed, status, _statuses, dayCompletions, Today),
                new CrossHooks(WriteAsync, RefreshAsync, animate => day.AnimateMarks = animate));
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
        if (day is null || day.DayIndex >= _statuses.Count || _statuses[day.DayIndex].CompletedSessions.Count == 0 || !_inFlight.Add(day.DayIndex))
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

            await WriteAsync(() => _enrollments.UncrossAsync(day.DayIndex, session));
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
            await WriteAsync(async () => logged = await _enrollments.LogBonusAsync(entry.WorkoutId, entry.Title, entry.Date, entry.DurationMinutes));
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

        await _journalDialogs.ShowDayAsync(day.DayIndex);
        await RefreshAsync();
    }

    [RelayCommand]
    private Task EditProgramNoteAsync() => _active is null ? Task.CompletedTask : _journalDialogs.EditProgramNoteAsync();

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
        if (_active is null || await _prompts.AskShiftDaysAsync() is not { } days)
        {
            return;
        }

        try
        {
            await WriteAsync(() => _enrollments.ShiftAsync(Today, days));
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
        CurrentStreak = 0;
        LongestStreak = 0;
        StreakText = "";
        LongestStreakText = "";
        HasEnrollment = false;
        ShowEmptyState = true;
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

    private void ScheduleMidnightRefresh()
    {
        _midnightTimer?.Dispose();
        _midnightTimer = null;
        if (!_isActive)
        {
            return;
        }

        var now = _time.GetLocalNow();
        var due = now.Date.AddDays(1) - now.DateTime + TimeSpan.FromSeconds(1);
        _midnightTimer = _time.CreateTimer(_ => Post(() => RunInBackground(RefreshAsync)), null, due, Timeout.InfiniteTimeSpan);
    }

    private void OnTrackingChanged()
    {
        if (Volatile.Read(ref _ownWrites) == 0)
        {
            Post(() => RunInBackground(RefreshAsync));
        }
    }

    private async Task WriteAsync(Func<Task> write)
    {
        Interlocked.Increment(ref _ownWrites);
        try
        {
            await write();
        }
        finally
        {
            Interlocked.Decrement(ref _ownWrites);
        }
    }

    // Fire-and-forget loads must never take the app down; log and keep showing the last good state.
    private async void RunInBackground(Func<Task> work)
    {
        try
        {
            await work();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Refreshing the program calendar failed.");
        }
    }

    // Timers and messages can arrive off the UI thread; the collections must change on it.
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
}
