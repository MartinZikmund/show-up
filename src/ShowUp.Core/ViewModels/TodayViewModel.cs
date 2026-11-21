using System.Globalization;
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

/// <summary>What's on today in every active program, grouped by program, with bonus workouts logged outside them.</summary>
public partial class TodayViewModel : ViewModelBase
{
    private const int RecentBonusDays = 7;

    private readonly IStringLocalizer _localizer;
    private readonly IActiveEnrollmentService _enrollments;
    private readonly IScheduleQuery _schedule;
    private readonly ICatalogService _catalog;
    private readonly TimeProvider _time;
    private readonly IMessenger _messenger;
    private readonly ICrossingCoordinator _crossing;
    private readonly IConfirmationDialogService _confirmation;
    private readonly IBonusWorkoutDialog _bonusDialog;
    private readonly ISectionNavigator _navigator;
    private readonly IJournalDialogs _journalDialogs;
    private readonly INavigationService _navigation;
    private readonly ILogger<TodayViewModel> _logger;

    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private DateOnly _loadedToday;
    private int _loadedGreetingHour = -1;
    private readonly LiveRefresher _live;

    public TodayViewModel(
        IStringLocalizer localizer,
        IActiveEnrollmentService enrollments,
        IScheduleQuery schedule,
        ICatalogService catalog,
        TimeProvider time,
        IMessenger messenger,
        ICrossingCoordinator crossing,
        IConfirmationDialogService confirmation,
        IBonusWorkoutDialog bonusDialog,
        ISectionNavigator navigator,
        IJournalDialogs journalDialogs,
        INavigationService navigation,
        ILogger<TodayViewModel> logger)
    {
        _localizer = localizer;
        _enrollments = enrollments;
        _schedule = schedule;
        _catalog = catalog;
        _time = time;
        _messenger = messenger;
        _crossing = crossing;
        _confirmation = confirmation;
        _bonusDialog = bonusDialog;
        _navigator = navigator;
        _journalDialogs = journalDialogs;
        _navigation = navigation;
        _logger = logger;
        _live = new(time, logger, "Refreshing Today failed.", RefreshAsync, NextChangeIn);
        PageTitle = localizer["Today"];
    }

    [ObservableProperty]
    public partial string Greeting { get; private set; } = "";

    /// <summary>One card group per active program, the selected one first.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSeveralPrograms))]
    public partial IReadOnlyList<TodayProgramViewModel> Programs { get; private set; } = [];

    public bool HasSeveralPrograms => Programs.Count > 1;

    /// <summary>The last week's bonus workouts, newest first.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasBonuses))]
    public partial IReadOnlyList<BonusWorkoutEntity> Bonuses { get; private set; } = [];

    /// <summary><see cref="Bonuses"/> with their detail lines, for the list.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<BonusItemViewModel> BonusItems { get; private set; } = [];

    public bool HasBonuses => Bonuses.Count > 0;

    [ObservableProperty]
    public partial bool HasEnrollment { get; private set; }

    /// <summary>Loaded and there's no enrollment (not shown while the first load runs).</summary>
    [ObservableProperty]
    public partial bool ShowNoEnrollment { get; private set; }

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
            _messenger.Register<TodayViewModel, TrackingChangedMessage>(this, static (r, _) => r._live.OnTrackingChanged());
            _messenger.Register<TodayViewModel, DataChangedMessage>(this, static (r, _) => r._live.OnDataChanged());
        }

        await RefreshAsync();
    }

    public void Unload()
    {
        _live.Deactivate();
        _messenger.UnregisterAll(this);
    }

    /// <summary>For window activation: the app may have slept through midnight.</summary>
    public Task RefreshIfDateChangedAsync() =>
        Today != _loadedToday || GreetingStart(_time.GetLocalNow().Hour) != _loadedGreetingHour ? RefreshAsync() : Task.CompletedTask;

    public async Task RefreshAsync()
    {
        await _refreshGate.WaitAsync();
        try
        {
            var today = Today;
            _loadedToday = today;
            _live.ScheduleNextChange();
            _loadedGreetingHour = GreetingStart(_time.GetLocalNow().Hour);
            Greeting = GreetingFor(_loadedGreetingHour);
            var recent = await _enrollments.GetBonusWorkoutsAsync(today.AddDays(1 - RecentBonusDays), today);
            Bonuses = [.. recent.OrderByDescending(b => b.Date).ThenByDescending(b => b.CreatedAt)];
            BonusItems = [.. Bonuses.Select(b => new BonusItemViewModel(b, b.Title, BonusDetail(b, today)))];

            var due = await _schedule.GetDueAsync(today);
            if (due.Count == 0)
            {
                ShowNoProgram();
                return;
            }

            var workouts = (await _catalog.GetCatalogAsync()).WorkoutsById;
            var existing = Programs.ToDictionary(p => p.EnrollmentId);
            List<TodayProgramViewModel> programs = [];
            foreach (var program in due)
            {
                var card = existing.GetValueOrDefault(program.Active.Id) ?? new TodayProgramViewModel(program.Active.Id);
                ShowProgram(card, program, workouts, today);
                programs.Add(card);
            }

            if (!Programs.SequenceEqual(programs))
            {
                Programs = programs;
            }

            HasEnrollment = true;
            ShowNoEnrollment = false;
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

        BonusWorkoutEntity? logged = null;
        try
        {
            await _live.WriteAsync(async () => logged = await _enrollments.LogBonusAsync(entry.WorkoutId, entry.Title, entry.Date, entry.DurationMinutes));
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning(ex, "The bonus workout was rejected.");
        }

        await RefreshAsync();
        if (logged?.WorkoutId is { } id && (await _catalog.GetCatalogAsync()).WorkoutsById.GetValueOrDefault(id) is { HasMetrics: true })
        {
            await _journalDialogs.LogBonusResultsAsync(logged);
        }
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

        await _live.WriteAsync(() => _enrollments.RemoveBonusAsync(bonus.Id));
        await RefreshAsync();
    }

    [RelayCommand]
    private void BrowsePrograms() => _navigator.NavigateToSection(NavigationSection.Programs);

    [RelayCommand]
    private void OpenPro() => _navigation.Navigate<ProViewModel>();

    private async Task CrossAsync(TodayProgramViewModel program, TodaySessionViewModel session)
    {
        var dayIndex = program.DayIndex;
        if (program.IsReadOnly || dayIndex < 0 || dayIndex >= program.Statuses.Count || !await program.CrossGate.WaitAsync(0))
        {
            return;
        }

        try
        {
            var status = program.Statuses[dayIndex];
            if (status.CompletedSessions.Contains(session.Index))
            {
                return;
            }

            var dayCompletions = program.Completions.Where(c => c.DayIndex == dayIndex).ToList();
            await _crossing.CrossAsync(
                new CrossTarget(
                    program.EnrollmentId,
                    dayIndex,
                    session.Index,
                    session.Name,
                    true,
                    CalendarDayViewModel.SeedFor(dayCompletions, dayIndex),
                    status,
                    program.Statuses,
                    dayCompletions,
                    Today),
                new CrossHooks(_live.WriteAsync, RefreshAsync, animate => program.AnimateMarks = animate));
        }
        catch (InvalidOperationException)
        {
            // The enrollment ended, went read-only or the day moved on under us; show what's true now.
            await RefreshAsync();
        }
        finally
        {
            program.AnimateMarks = true;
            program.CrossGate.Release();
        }
    }

    private void ShowProgram(TodayProgramViewModel card, DueProgram due, IReadOnlyDictionary<string, Workout> workouts, DateOnly today)
    {
        var active = due.Active;
        card.Statuses = due.Statuses;
        card.Completions = due.Completions;
        card.ProgramName = active.Program.Name;
        card.IsReadOnly = due.IsReadOnly;
        card.CurrentStreak = StreakCalculator.Current(due.Statuses, today);
        card.StreakText = _localizer["CrossMomentStreakFormat", card.CurrentStreak].Value;

        if (due.Day is not { } status)
        {
            card.DayIndex = -1;
            card.IsOffSchedule = true;
            card.IsRestDay = false;
            card.DayNumber = null;
            card.DayText = "";
            card.Sessions = [];
            card.MarkStrokes = 0;
            card.Inks = [];
            return;
        }

        card.DayIndex = status.DayIndex;
        card.IsOffSchedule = false;
        card.IsRestDay = status.CountsAsRest;
        card.DayNumber = status.DayIndex + 1;
        card.DayText = _localizer["TodayDayFormat", status.DayIndex + 1, active.Calendar.DayCount].Value;

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
                _localizer["TodayCrossOffFormat", workout?.Name ?? scheduled[i].WorkoutId].Value,
                scheduled[i].Optional,
                status.CompletedSessions.Contains(i),
                s => CrossAsync(card, s))
            {
                Program = card,
                IsReadOnly = due.IsReadOnly,
            });
        }

        var lastDone = status.CompletedSessions.Count > 0 ? status.CompletedSessions.Max() : -1;
        foreach (var session in sessions)
        {
            session.ShowsMarks = session.Index == lastDone;
        }

        UpdateSessions(card, sessions);

        var dayCompletions = due.DayCompletions;
        var autos = dayCompletions.Where(c => c.InkJson is null).Select(c => c.SessionIndex).ToHashSet();
        IReadOnlyList<string> inks = [.. dayCompletions.Where(c => c.InkJson is not null).Select(c => c.InkJson!)];
        card.Seed = CalendarDayViewModel.SeedFor(dayCompletions, status.DayIndex);
        if (!card.Inks.SequenceEqual(inks))
        {
            card.Inks = inks;
        }

        card.MarkStrokes = MarkerAppearance.StrokesFor(status, autos);
    }

    // Same sessions as before: flip IsDone in place so a card only animates what changed.
    private static void UpdateSessions(TodayProgramViewModel card, List<TodaySessionViewModel> fresh)
    {
        var current = card.Sessions;
        if (current.Count == fresh.Count && current.Zip(fresh).All(p => p.First.Name == p.Second.Name))
        {
            foreach (var (old, update) in current.Zip(fresh))
            {
                old.IsDone = update.IsDone;
                old.ShowsMarks = update.ShowsMarks;
                old.IsReadOnly = update.IsReadOnly;
            }

            return;
        }

        card.Sessions = fresh;
    }

    private void ShowNoProgram()
    {
        Programs = [];
        HasEnrollment = false;
        ShowNoEnrollment = true;
    }

    // The hour a greeting starts at, so the timer and the refresh check agree on when it changes.
    private static int GreetingStart(int hour) => hour switch
    {
        >= 5 and < 12 => 5,
        >= 12 and < 18 => 12,
        >= 18 and < 22 => 18,
        _ => 22,
    };

    private string GreetingFor(int startHour) => _localizer[startHour switch
    {
        5 => "GreetingMorning",
        12 => "GreetingAfternoon",
        18 => "GreetingEvening",
        _ => "GreetingNight",
    }].Value;

    private string BonusDetail(BonusWorkoutEntity bonus, DateOnly today)
    {
        var day = bonus.Date == today ? _localizer["TodayBonusToday"].Value : bonus.Date.ToString("ddd d MMM", CultureInfo.CurrentCulture);
        return bonus.DurationMinutes is { } minutes ? $"{day} · {_localizer["TodayDurationFormat", minutes].Value}" : day;
    }

    // Next change of day or greeting, whichever comes first.
    private static TimeSpan NextChangeIn(DateTimeOffset now)
    {
        var nextHour = new[] { 5, 12, 18, 22 }.Where(h => h > now.Hour).DefaultIfEmpty(24).First();
        return now.Date.AddHours(nextHour) - now.DateTime;
    }
}
