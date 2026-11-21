using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Messaging;
using ShowUp.Core.Catalog;
using ShowUp.Core.Marking;
using ShowUp.Core.Messages;
using ShowUp.Core.Navigation;
using ShowUp.Core.Services;
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
    private readonly IAppPreferences _preferences;
    private readonly ICrossMoment _crossMoment;
    private readonly IMarkingFeedback _feedback;
    private readonly IConfirmationDialogService _confirmation;
    private readonly ICalendarPrompts _prompts;
    private readonly ISectionNavigator _navigator;

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

    public ProgramCalendarViewModel(
        IStringLocalizer localizer,
        IActiveEnrollmentService enrollments,
        ICatalogService catalog,
        TimeProvider time,
        IMessenger messenger,
        IAppPreferences preferences,
        ICrossMoment crossMoment,
        IMarkingFeedback feedback,
        IConfirmationDialogService confirmation,
        ICalendarPrompts prompts,
        ISectionNavigator navigator)
    {
        _localizer = localizer;
        _enrollments = enrollments;
        _catalog = catalog;
        _time = time;
        _messenger = messenger;
        _preferences = preferences;
        _crossMoment = crossMoment;
        _feedback = feedback;
        _confirmation = confirmation;
        _prompts = prompts;
        _navigator = navigator;
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

    public bool IsDebug =>
#if DEBUG
        true;
#else
        false;
#endif

    private DateOnly Today => DateOnly.FromDateTime(_time.GetLocalNow().DateTime);

    public override void ViewLoaded()
    {
        base.ViewLoaded();
        _ = LoadAsync();
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
            _messenger.Register<ProgramCalendarViewModel, TrackingChangedMessage>(this, static (r, _) => r.Post(r.RefreshInBackground));
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

    /// <summary>Crosses the day's lowest undone session: draw-it-yourself or the auto marker, per the user's style.</summary>
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

        if (day.IsComplete || !_inFlight.Add(day.DayIndex))
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
            var autos = dayCompletions.Where(c => c.InkJson is null).Select(c => c.SessionIndex).ToHashSet();
            var (before, afterAuto, afterDrawn) = MarkerAppearance.Outcomes(status, autos, session);
            var info = MomentInfo(day, status, session, before, afterAuto);

            if (_preferences.CrossOffStyle == CrossOffStyle.DrawYourself)
            {
                List<string> priorInks = [.. dayCompletions.Where(c => c.InkJson is not null).Select(c => c.InkJson!)];
                var result = await _crossMoment.DrawAsync(new CrossMomentDrawRequest(info, priorInks, afterDrawn));
                if (!result.Crossed)
                {
                    return;
                }

                day.AnimateMarks = false;
                await _enrollments.CrossAsync(day.DayIndex, session, result.InkJson);
                await RefreshAsync();
            }
            else
            {
                var showMoment = CrossMomentPolicy.ShouldShow(_preferences.CrossMomentMode, day.IsToday);
                day.AnimateMarks = !showMoment;
                await _enrollments.CrossAsync(day.DayIndex, session);
                await RefreshAsync();
                if (showMoment)
                {
                    await _crossMoment.ShowAsync(info with { Seed = day.Seed, CurrentStreak = CurrentStreak });
                }
                else if (afterAuto == before)
                {
                    _feedback.SessionTicked();
                }
            }
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

            await _enrollments.UncrossAsync(day.DayIndex, session);
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
        if (day is null || day.IsFuture || await _prompts.AskBonusTitleAsync() is not { } title || string.IsNullOrWhiteSpace(title))
        {
            return;
        }

        await _enrollments.LogBonusAsync(title.Trim(), day.Date);
        await RefreshAsync();
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
            await _enrollments.ShiftAsync(Today, days);
        }
        catch (InvalidOperationException)
        {
            // Nothing left to shift (every remaining day is crossed or past the end).
        }

        await RefreshAsync();
    }

    [RelayCommand]
    private void BrowsePrograms() => _navigator.NavigateToSection(NavigationSection.Programs);

    /// <summary>Debug builds: Insanity (classic) started 9 days ago, so the grid has past, today and future days.</summary>
    [RelayCommand]
    private async Task StartSampleEnrollmentAsync()
    {
#if DEBUG
        if (await _catalog.GetProgramAsync("insanity") is { } program && program.Tracks.FirstOrDefault(t => t.Id == "classic") is { } track)
        {
            await _enrollments.EnrollAsync(program, track, Today.AddDays(-9));
            await RefreshAsync();
        }
#else
        await Task.CompletedTask;
#endif
    }

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

    private CrossMomentInfo MomentInfo(CalendarDayViewModel day, DayStatus status, int session, int before, int afterAuto)
    {
        var after = status with { CompletedSessions = new HashSet<int>(status.CompletedSessions) { session } };
        List<DayStatus> statusesAfter = [.. _statuses];
        statusesAfter[day.DayIndex] = after;

        return new CrossMomentInfo(
            day.DayNumber,
            _statuses.Count,
            SessionName(day.DayIndex, session),
            before,
            afterAuto,
            day.Seed,
            after.CompletedSessions.Count(i => !after.OptionalSessions.Contains(i)),
            after.RequiredCount,
            StreakCalculator.Current(statusesAfter, Today));
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
        _midnightTimer = _time.CreateTimer(_ => Post(RefreshInBackground), null, due, Timeout.InfiniteTimeSpan);
    }

    private void RefreshInBackground() => _ = RefreshAsync();

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
