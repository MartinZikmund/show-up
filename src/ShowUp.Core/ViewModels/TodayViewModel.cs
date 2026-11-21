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

namespace ShowUp.Core.ViewModels;

/// <summary>What's on today in the active program, with bonus workouts logged outside it.</summary>
public partial class TodayViewModel : ViewModelBase
{
    private const int RecentBonusDays = 7;

    private readonly IStringLocalizer _localizer;
    private readonly IActiveEnrollmentService _enrollments;
    private readonly ICatalogService _catalog;
    private readonly TimeProvider _time;
    private readonly IMessenger _messenger;
    private readonly ICrossingCoordinator _crossing;
    private readonly IConfirmationDialogService _confirmation;
    private readonly IBonusWorkoutDialog _bonusDialog;
    private readonly ISectionNavigator _navigator;
    private readonly ILogger<TodayViewModel> _logger;

    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private readonly SemaphoreSlim _crossGate = new(1, 1);
    private IReadOnlyList<DayStatus> _statuses = [];
    private IReadOnlyList<CompletedSession> _completions = [];
    private int _dayIndex = -1;
    private DateOnly _loadedToday;
    private SynchronizationContext? _context;
    private ITimer? _midnightTimer;
    private bool _isActive;

    // While this view model writes, it refreshes itself; the echoed TrackingChangedMessage is skipped.
    private int _ownWrites;

    public TodayViewModel(
        IStringLocalizer localizer,
        IActiveEnrollmentService enrollments,
        ICatalogService catalog,
        TimeProvider time,
        IMessenger messenger,
        ICrossingCoordinator crossing,
        IConfirmationDialogService confirmation,
        IBonusWorkoutDialog bonusDialog,
        ISectionNavigator navigator,
        ILogger<TodayViewModel> logger)
    {
        _localizer = localizer;
        _enrollments = enrollments;
        _catalog = catalog;
        _time = time;
        _messenger = messenger;
        _crossing = crossing;
        _confirmation = confirmation;
        _bonusDialog = bonusDialog;
        _navigator = navigator;
        _logger = logger;
        PageTitle = localizer["Today"];
    }

    [ObservableProperty]
    public partial string Greeting { get; private set; } = "";

    [ObservableProperty]
    public partial string? ProgramName { get; private set; }

    [ObservableProperty]
    public partial int? DayNumber { get; private set; }

    /// <summary>"Day 12 of 60", empty when today isn't a program day.</summary>
    [ObservableProperty]
    public partial string DayText { get; private set; } = "";

    [ObservableProperty]
    public partial IReadOnlyList<TodaySessionViewModel> Sessions { get; private set; } = [];

    /// <summary>The last week's bonus workouts, newest first.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasBonuses))]
    public partial IReadOnlyList<BonusWorkoutEntity> Bonuses { get; private set; } = [];

    public bool HasBonuses => Bonuses.Count > 0;

    [ObservableProperty]
    public partial int CurrentStreak { get; private set; }

    [ObservableProperty]
    public partial string StreakText { get; private set; } = "";

    /// <summary>Today is a rest day (or all of its sessions are optional).</summary>
    [ObservableProperty]
    public partial bool IsRestDay { get; private set; }

    [ObservableProperty]
    public partial bool HasEnrollment { get; private set; }

    /// <summary>An enrollment, but nothing scheduled today: before it starts, after it ends, or a gap from a shift.</summary>
    [ObservableProperty]
    public partial bool IsOffSchedule { get; private set; }

    /// <summary>Loaded and there's no enrollment (not shown while the first load runs).</summary>
    [ObservableProperty]
    public partial bool ShowNoEnrollment { get; private set; }

    /// <summary>Today's generated marker strokes; drawn crosses are in <see cref="Inks"/>.</summary>
    [ObservableProperty]
    public partial int MarkStrokes { get; private set; }

    [ObservableProperty]
    public partial IReadOnlyList<string> Inks { get; private set; } = [];

    [ObservableProperty]
    public partial int Seed { get; private set; }

    /// <summary>False while a cross moment shows the cross big, so the card lands it without animating.</summary>
    public bool AnimateMarks { get; private set; } = true;

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
            _messenger.Register<TodayViewModel, TrackingChangedMessage>(this, static (r, _) => r.OnTrackingChanged());
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
            Greeting = GreetingFor(_time.GetLocalNow().Hour);
            var recent = await _enrollments.GetBonusWorkoutsAsync(today.AddDays(1 - RecentBonusDays), today);
            Bonuses = [.. recent.OrderByDescending(b => b.Date).ThenByDescending(b => b.CreatedAt)];

            var active = await _enrollments.GetAsync();
            if (active is null)
            {
                ShowNoProgram();
                return;
            }

            var workouts = (await _catalog.GetCatalogAsync()).WorkoutsById;
            var completions = await _enrollments.GetCompletionsAsync();
            var statuses = ProgressCalculator.Build(
                active.Track,
                workouts,
                active.Calendar,
                completions.Select(c => (c.DayIndex, c.SessionIndex)).ToHashSet());

            _statuses = statuses;
            _completions = completions;
            ProgramName = active.Program.Name;
            CurrentStreak = StreakCalculator.Current(statuses, today);
            StreakText = _localizer["CrossMomentStreakFormat", CurrentStreak].Value;
            HasEnrollment = true;
            ShowNoEnrollment = false;

            if (active.Calendar.DayIndexOn(today) is not { } dayIndex)
            {
                ShowNothingScheduled();
                return;
            }

            ShowDay(active, workouts, statuses[dayIndex], [.. completions.Where(c => c.DayIndex == dayIndex)]);
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    /// <summary>Opens the bonus dialog for today and logs what it returns.</summary>
    [RelayCommand]
    private async Task LogBonusAsync()
    {
        if (await _bonusDialog.ShowAsync(Today) is not { } entry)
        {
            return;
        }

        try
        {
            await WriteAsync(() => _enrollments.LogBonusAsync(entry.WorkoutId, entry.Title, entry.Date, entry.DurationMinutes));
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning(ex, "The bonus workout was rejected.");
        }

        await RefreshAsync();
    }

    [RelayCommand]
    private async Task RemoveBonusAsync(BonusWorkoutEntity? bonus)
    {
        if (bonus is null)
        {
            return;
        }

        var confirmed = await _confirmation.ShowAsync(
            _localizer["BonusRemoveConfirmTitle"],
            _localizer["BonusRemoveConfirmFormat", bonus.Title]);
        if (confirmed != ConfirmationResult.Confirmed)
        {
            return;
        }

        await WriteAsync(() => _enrollments.RemoveBonusAsync(bonus.Id));
        await RefreshAsync();
    }

    [RelayCommand]
    private void BrowsePrograms() => _navigator.NavigateToSection(NavigationSection.Programs);

    private async Task CrossAsync(TodaySessionViewModel session)
    {
        if (_dayIndex < 0 || _dayIndex >= _statuses.Count || !await _crossGate.WaitAsync(0))
        {
            return;
        }

        try
        {
            var status = _statuses[_dayIndex];
            if (status.CompletedSessions.Contains(session.Index))
            {
                return;
            }

            var dayCompletions = _completions.Where(c => c.DayIndex == _dayIndex).ToList();
            await _crossing.CrossAsync(
                new CrossTarget(_dayIndex, session.Index, session.Name, true, CalendarDayViewModel.SeedFor(dayCompletions, _dayIndex), status, _statuses, dayCompletions, Today),
                new CrossHooks(WriteAsync, RefreshAsync, animate => AnimateMarks = animate));
        }
        catch (InvalidOperationException)
        {
            // The enrollment ended or the day moved on under us; show what's true now.
            await RefreshAsync();
        }
        finally
        {
            AnimateMarks = true;
            _crossGate.Release();
        }
    }

    private void ShowDay(ActiveEnrollment active, IReadOnlyDictionary<string, Workout> workouts, DayStatus status, IReadOnlyList<CompletedSession> dayCompletions)
    {
        _dayIndex = status.DayIndex;
        IsOffSchedule = false;
        IsRestDay = status.CountsAsRest;
        DayNumber = status.DayIndex + 1;
        DayText = _localizer["TodayDayFormat", status.DayIndex + 1, active.Calendar.DayCount].Value;

        var scheduled = _catalog.GetTrackDays(active.Track)[status.DayIndex].Sessions;
        List<TodaySessionViewModel> sessions = [];
        for (var i = 0; i < scheduled.Count; i++)
        {
            var workout = workouts.GetValueOrDefault(scheduled[i].WorkoutId);
            var kind = workout?.Kind ?? WorkoutKind.Mixed;
            sessions.Add(new TodaySessionViewModel(
                i,
                workout?.Name ?? scheduled[i].WorkoutId,
                workout?.DurationMinutes,
                kind,
                _localizer[$"WorkoutKind_{kind}"].Value,
                workout?.DurationMinutes is { } minutes ? _localizer["TodayDurationFormat", minutes].Value : "",
                scheduled[i].Optional,
                status.CompletedSessions.Contains(i),
                CrossAsync));
        }

        UpdateSessions(sessions);

        var autos = dayCompletions.Where(c => c.InkJson is null).Select(c => c.SessionIndex).ToHashSet();
        IReadOnlyList<string> inks = [.. dayCompletions.Where(c => c.InkJson is not null).Select(c => c.InkJson!)];
        Seed = CalendarDayViewModel.SeedFor(dayCompletions, status.DayIndex);
        if (!Inks.SequenceEqual(inks))
        {
            Inks = inks;
        }

        MarkStrokes = MarkerAppearance.StrokesFor(status, autos);
    }

    // Same sessions as before: flip IsDone in place so a card only animates what changed.
    private void UpdateSessions(List<TodaySessionViewModel> fresh)
    {
        if (Sessions.Count == fresh.Count && Sessions.Zip(fresh).All(p => p.First.Name == p.Second.Name))
        {
            foreach (var (old, update) in Sessions.Zip(fresh))
            {
                old.IsDone = update.IsDone;
            }

            return;
        }

        Sessions = fresh;
    }

    private void ShowNothingScheduled()
    {
        _dayIndex = -1;
        IsOffSchedule = true;
        IsRestDay = false;
        DayNumber = null;
        DayText = "";
        ClearDay();
    }

    private void ShowNoProgram()
    {
        _dayIndex = -1;
        _statuses = [];
        _completions = [];
        ProgramName = null;
        DayNumber = null;
        DayText = "";
        CurrentStreak = 0;
        StreakText = "";
        HasEnrollment = false;
        IsOffSchedule = false;
        IsRestDay = false;
        ShowNoEnrollment = true;
        ClearDay();
    }

    private void ClearDay()
    {
        Sessions = [];
        MarkStrokes = 0;
        Inks = [];
    }

    private string GreetingFor(int hour) => _localizer[hour switch
    {
        >= 5 and < 12 => "GreetingMorning",
        >= 12 and < 18 => "GreetingAfternoon",
        >= 18 and < 22 => "GreetingEvening",
        _ => "GreetingNight",
    }].Value;

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
            _logger.LogError(ex, "Refreshing Today failed.");
        }
    }

    // Timers and messages can arrive off the UI thread; the properties must change on it.
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
