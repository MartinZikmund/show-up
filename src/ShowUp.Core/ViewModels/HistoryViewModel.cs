using System.Globalization;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging;
using ShowUp.Core.Catalog;
using ShowUp.Core.Entitlements;
using ShowUp.Core.Localization;
using ShowUp.Core.Messages;
using ShowUp.Core.Navigation;
using ShowUp.Core.Services;
using ShowUp.Core.Storage;
using ShowUp.Core.Tracking;

namespace ShowUp.Core.ViewModels;

public enum HistoryStatus
{
    Active,
    Finished,
    Stopped,
}

/// <summary>One program in the history, current or ended.</summary>
public sealed record HistoryProgramViewModel(
    Guid EnrollmentId,
    string Name,
    string AccentColor,
    HistoryStatus Status,
    string StatusText,
    string ProgressText,
    string CrossedText,
    string BestText,
    string DatesText,
    bool HasResults,
    string CalendarAutomationName,
    string ResultsAutomationName)
{
    public bool IsActive => Status == HistoryStatus.Active;

    public bool ShowResultsLink => IsActive && HasResults;

    public string SummaryText => $"{ProgressText} · {CrossedText}";

    public string AutomationId => $"HistoryProgram_{EnrollmentId}";

    public string CalendarAutomationId => $"HistoryCalendar_{EnrollmentId}";

    public string ResultsAutomationId => $"HistoryResults_{EnrollmentId}";

    public string AutomationName => $"{Name}, {StatusText}, {ProgressText}, {CrossedText}, {BestText}, {DatesText}";
}

public sealed record HistoryBonusViewModel(string Title, string DurationText, string DateText, string AutomationName);

public sealed record HistoryMonthViewModel(string Header, IReadOnlyList<HistoryBonusViewModel> Items)
{
    public string AutomationId => $"HistoryMonth_{Header}";
}

/// <summary>Programs started so far and bonus workouts by month. Free sees the current program and the last 30 days of bonuses.</summary>
public partial class HistoryViewModel : ViewModelBase
{
    public const int FreeBonusDays = 30;

    private readonly IActiveEnrollmentService _enrollments;
    private readonly ICatalogService _catalog;
    private readonly IEntitlementService _entitlements;
    private readonly INavigationService _navigation;
    private readonly ISectionNavigator _navigator;
    private readonly IMessenger _messenger;
    private readonly IStringLocalizer _localizer;
    private readonly TimeProvider _time;
    private readonly LiveRefresher _live;
    private readonly SemaphoreSlim _loadGate = new(1, 1);

    public HistoryViewModel(
        IActiveEnrollmentService enrollments,
        ICatalogService catalog,
        IEntitlementService entitlements,
        INavigationService navigation,
        ISectionNavigator navigator,
        IMessenger messenger,
        IStringLocalizer localizer,
        TimeProvider time,
        ILogger<HistoryViewModel> logger)
    {
        _enrollments = enrollments;
        _catalog = catalog;
        _entitlements = entitlements;
        _navigation = navigation;
        _navigator = navigator;
        _messenger = messenger;
        _localizer = localizer;
        _time = time;
        _live = new(time, logger, "Loading the history failed.", LoadAsync, now => now.Date.AddDays(1) - now.DateTime);
        PageTitle = localizer["History"];
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPrograms))]
    public partial IReadOnlyList<HistoryProgramViewModel> Programs { get; private set; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasBonuses))]
    public partial IReadOnlyList<HistoryMonthViewModel> BonusMonths { get; private set; } = [];

    /// <summary>Free and there is more history than it shows.</summary>
    [ObservableProperty]
    public partial bool ShowGate { get; private set; }

    [ObservableProperty]
    public partial bool ShowEmptyState { get; private set; }

    public bool HasPrograms => Programs.Count > 0;

    public bool HasBonuses => BonusMonths.Count > 0;

    private DateOnly Today => DateOnly.FromDateTime(_time.GetLocalNow().DateTime);

    public override void ViewLoaded()
    {
        base.ViewLoaded();
        _live.RunInBackground(StartAsync);
    }

    public override void ViewUnloaded()
    {
        base.ViewUnloaded();
        _live.Deactivate();
        _messenger.UnregisterAll(this);
    }

    private async Task StartAsync()
    {
        if (_live.Activate())
        {
            _messenger.Register<HistoryViewModel, TrackingChangedMessage>(this, static (r, _) => r._live.OnTrackingChanged());
            _messenger.Register<HistoryViewModel, DataChangedMessage>(this, static (r, _) => r._live.OnDataChanged());
        }

        await LoadAsync();
    }

    public async Task LoadAsync()
    {
        await _loadGate.WaitAsync();
        try
        {
            await LoadCoreAsync();
            _live.ScheduleNextChange();
        }
        finally
        {
            _loadGate.Release();
        }
    }

    private async Task LoadCoreAsync()
    {
        var today = Today;
        var isPro = FeatureGate.CanUse(ProFeature.FullHistory, _entitlements.IsPro);
        var workouts = (await _catalog.GetCatalogAsync()).WorkoutsById;

        var records = await _enrollments.GetHistoryAsync();
        IReadOnlyList<ActiveEnrollment> shown = records;
        if (!isPro)
        {
            // The current program is the primary one; with none active, the most recent ended one stands in.
            var primaryId = (await _enrollments.GetPrimaryAsync())?.Id;
            var current = records.FirstOrDefault(e => e.Id == primaryId) ?? records.FirstOrDefault();
            shown = current is null ? [] : [current];
        }

        List<HistoryProgramViewModel> programs = [];
        foreach (var enrollment in shown)
        {
            programs.Add(ToProgram(enrollment, await _enrollments.GetHistoryCompletionsAsync(enrollment.Id), workouts, today));
        }

        var bonuses = await _enrollments.GetBonusWorkoutsAsync(DateOnly.MinValue, today);
        var from = today.AddDays(1 - FreeBonusDays);
        var visibleBonuses = isPro ? bonuses : [.. bonuses.Where(b => b.Date >= from)];

        Programs = programs;
        BonusMonths = GroupByMonth(visibleBonuses);
        ShowGate = !isPro && (shown.Count < records.Count || visibleBonuses.Count < bonuses.Count);
        ShowEmptyState = programs.Count == 0 && visibleBonuses.Count == 0 && !ShowGate;
    }

    [RelayCommand]
    private void OpenResults(HistoryProgramViewModel? program)
    {
        if (program is { ShowResultsLink: true })
        {
            _navigation.Navigate<ProgramResultsViewModel>(program.EnrollmentId);
        }
    }

    [RelayCommand]
    private async Task OpenCalendarAsync(HistoryProgramViewModel? program)
    {
        if (program is not { IsActive: true })
        {
            return;
        }

        try
        {
            await _enrollments.SelectAsync(program.EnrollmentId);
        }
        catch (InvalidOperationException)
        {
            return;
        }

        _navigator.NavigateToSection(NavigationSection.Calendar);
    }

    [RelayCommand]
    private void OpenPro() => _navigation.Navigate<ProViewModel>();

    [RelayCommand]
    private void OpenStats() => _navigation.Navigate<StatsViewModel>();

    private HistoryProgramViewModel ToProgram(ActiveEnrollment active, IReadOnlyList<CompletedSession> completions, IReadOnlyDictionary<string, Workout> workouts, DateOnly today)
    {
        var enrollment = active.Enrollment;
        var isActive = enrollment.EndedAt is null;
        var calendar = active.Calendar;
        var completed = completions.Select(c => (c.DayIndex, c.SessionIndex)).ToHashSet();
        var statuses = ProgressCalculator.Build(active.Track, workouts, calendar, completed);
        var crossed = statuses.Count(s => s.Progress == DayProgress.Complete);
        var best = StreakCalculator.Longest(statuses, today);

        var status = isActive ? HistoryStatus.Active
            : enrollment.EndReason == EnrollmentEndReason.Abandoned ? HistoryStatus.Stopped
            : HistoryStatus.Finished;

        string progress;
        if (isActive)
        {
            var reached = Enumerable.Range(0, calendar.DayCount).Count(i => calendar.DateOf(i) <= today);
            progress = reached == 0
                ? _localizer["HistoryNotStarted"].Value
                : _localizer["TodayDayFormat", reached, calendar.DayCount].Value;
        }
        else
        {
            progress = _localizer.Format("ProgramDays", calendar.DayCount);
        }

        var started = FormatDate(enrollment.StartDate);
        var dates = enrollment.EndedAt is { } ended
            ? _localizer["HistoryRangeFormat", started, FormatDate(LocalDateOf(ended))].Value
            : _localizer["HistoryStartedFormat", started].Value;

        var hasResults = isActive && _catalog.GetTrackDays(active.Track)
            .Any(d => d.Sessions.Any(s => workouts.GetValueOrDefault(s.WorkoutId) is { HasMetrics: true }));

        return new HistoryProgramViewModel(
            enrollment.Id,
            active.Program.Name,
            active.Program.AccentColor,
            status,
            _localizer["HistoryStatus" + status].Value,
            progress,
            _localizer["HistoryCrossedFormat", crossed].Value,
            _localizer["CalendarLongestStreakFormat", best].Value,
            dates,
            hasResults,
            _localizer["HistoryCalendarNameFormat", active.Program.Name].Value,
            _localizer["HistoryResultsNameFormat", active.Program.Name].Value);
    }

    private IReadOnlyList<HistoryMonthViewModel> GroupByMonth(IReadOnlyList<BonusWorkoutEntity> bonuses) =>
    [
        .. bonuses
            .GroupBy(b => (b.Date.Year, b.Date.Month))
            .OrderByDescending(g => g.Key)
            .Select(g => new HistoryMonthViewModel(
                MonthName(new DateOnly(g.Key.Year, g.Key.Month, 1)),
                [.. g.OrderByDescending(b => b.Date).ThenByDescending(b => b.CreatedAt).Select(ToBonus)])),
    ];

    private HistoryBonusViewModel ToBonus(BonusWorkoutEntity bonus)
    {
        var format = CultureInfo.CurrentCulture.DateTimeFormat;
        var date = $"{bonus.Date.ToString("ddd", format)} {bonus.Date.ToString(format.MonthDayPattern, format)}";
        var duration = bonus.DurationMinutes is { } minutes ? _localizer["TodayDurationFormat", minutes].Value : "";
        return new(bonus.Title, duration, date, duration.Length == 0 ? $"{bonus.Title}, {date}" : $"{bonus.Title}, {duration}, {date}");
    }

    private static string FormatDate(DateOnly date) => date.ToString("d", CultureInfo.CurrentCulture);

    private DateOnly LocalDateOf(DateTimeOffset moment) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(moment, _time.LocalTimeZone).DateTime);

    private static string MonthName(DateOnly date)
    {
        var format = CultureInfo.CurrentCulture.DateTimeFormat;
        var text = date.ToString(format.YearMonthPattern, format);
        return char.ToUpper(text[0], CultureInfo.CurrentCulture) + text[1..];
    }
}
