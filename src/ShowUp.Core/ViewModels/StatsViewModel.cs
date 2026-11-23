using System.Globalization;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging;
using ShowUp.Core.Entitlements;
using ShowUp.Core.Messages;
using ShowUp.Core.Navigation;
using ShowUp.Core.Services;
using ShowUp.Core.Stats;

namespace ShowUp.Core.ViewModels;

/// <param name="Fraction">0 to 1, the bar's length against the longest one.</param>
/// <param name="DetailText">A second line under the label (time, where known); empty for none.</param>
public sealed record StatsBarViewModel(string Label, double Fraction, string ValueText, string AutomationName, string DetailText = "")
{
    public bool HasDetail => DetailText.Length > 0;
}

/// <param name="Level">0 (nothing) to 3 (three workouts or more); the count is in <paramref name="AutomationName"/>, never only in the colour.</param>
/// <param name="IsFuture">A day of the current week that hasn't come yet.</param>
public sealed record StatsHeatCellViewModel(int Level, bool IsFuture, string AutomationName);

public sealed record StatsHeatWeekViewModel(IReadOnlyList<StatsHeatCellViewModel> Days);

public sealed record StatsProgramViewModel(Guid EnrollmentId, string Name, double Fraction, string RateText, string DetailText)
{
    public string AutomationId => $"StatsProgram_{EnrollmentId}";

    public string AutomationName => $"{Name}, {RateText}, {DetailText}";
}

/// <summary>Workouts per week, minutes by kind, a consistency heatmap and how the programs compare. Free sees four weeks of the current program.</summary>
public partial class StatsViewModel : ViewModelBase
{
    public const int MaxHeatLevel = 3;

    private readonly IStatsService _stats;
    private readonly IEntitlementService _entitlements;
    private readonly INavigationService _navigation;
    private readonly IMessenger _messenger;
    private readonly IStringLocalizer _localizer;
    private readonly TimeProvider _time;
    private readonly LiveRefresher _live;
    private readonly SemaphoreSlim _loadGate = new(1, 1);

    public StatsViewModel(
        IStatsService stats,
        IEntitlementService entitlements,
        INavigationService navigation,
        IMessenger messenger,
        IStringLocalizer localizer,
        TimeProvider time,
        ILogger<StatsViewModel> logger)
    {
        _stats = stats;
        _entitlements = entitlements;
        _navigation = navigation;
        _messenger = messenger;
        _localizer = localizer;
        _time = time;
        _live = new(time, logger, "Loading the statistics failed.", LoadAsync, now => now.Date.AddDays(1) - now.DateTime);
        PageTitle = localizer["Statistics"];
    }

    [ObservableProperty]
    public partial string TotalWorkoutsText { get; private set; } = "";

    [ObservableProperty]
    public partial string TotalTimeText { get; private set; } = "";

    [ObservableProperty]
    public partial string ActiveDaysText { get; private set; } = "";

    [ObservableProperty]
    public partial IReadOnlyList<StatsBarViewModel> WeekBars { get; private set; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasKinds))]
    public partial IReadOnlyList<StatsBarViewModel> KindBars { get; private set; } = [];

    /// <summary>Oldest week first; each week is seven days starting on the culture's first day of the week.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasHeatmap))]
    public partial IReadOnlyList<StatsHeatWeekViewModel> HeatWeeks { get; private set; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPrograms))]
    public partial IReadOnlyList<StatsProgramViewModel> Programs { get; private set; } = [];

    /// <summary>Free: the Pro sections are replaced by one gate row.</summary>
    [ObservableProperty]
    public partial bool ShowGate { get; private set; }

    [ObservableProperty]
    public partial bool ShowEmptyState { get; private set; }

    public bool HasKinds => KindBars.Count > 0;

    public bool HasHeatmap => HeatWeeks.Count > 0;

    public bool HasPrograms => Programs.Count > 0;

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
            _messenger.Register<StatsViewModel, TrackingChangedMessage>(this, static (r, _) => r._live.OnTrackingChanged());
            _messenger.Register<StatsViewModel, DataChangedMessage>(this, static (r, _) => r._live.OnDataChanged());
        }

        await LoadAsync();
    }

    public async Task LoadAsync()
    {
        await _loadGate.WaitAsync();
        try
        {
            var isPro = FeatureGate.CanUse(ProFeature.FullHistory, _entitlements.IsPro);
            Show(await _stats.GetAsync(isPro), isPro);
            _live.ScheduleNextChange();
        }
        finally
        {
            _loadGate.Release();
        }
    }

    [RelayCommand]
    private void OpenPro() => _navigation.Navigate<ProViewModel>();

    [RelayCommand]
    private void GoBack() => _navigation.GoBack();

    private void Show(StatsSummary summary, bool isPro)
    {
        var today = Today;
        var culture = CultureInfo.CurrentCulture;

        TotalWorkoutsText = summary.TotalWorkouts.ToString("N0", culture);
        TotalTimeText = Duration(summary.TotalMinutes);
        ActiveDaysText = summary.ActiveDays.ToString("N0", culture);

        int busiest = summary.Weeks.Count == 0 ? 0 : summary.Weeks.Max(w => w.Workouts);
        WeekBars =
        [
            .. summary.Weeks.Select(w => new StatsBarViewModel(
                ShortDate(w.WeekStart),
                Fraction(w.Workouts, busiest),
                w.Workouts.ToString("N0", culture),
                _localizer["StatsWeekName", w.WeekStart.ToString("M", culture), w.Workouts, w.Minutes].Value)),
        ];

        // Bars follow the workout count; time is a second line, only where the length is known.
        int most = summary.ByKind.Count == 0 ? 0 : summary.ByKind.Max(k => k.Workouts);
        KindBars =
        [
            .. summary.ByKind.Select(k =>
            {
                string kind = _localizer[$"WorkoutKind_{k.Kind}"].Value;
                return new StatsBarViewModel(
                    kind,
                    Fraction(k.Workouts, most),
                    k.Workouts.ToString("N0", culture),
                    _localizer["StatsKindName", kind, k.Workouts, k.Minutes].Value,
                    k.Minutes > 0 ? Duration(k.Minutes) : "");
            }),
        ];

        HeatWeeks =
        [
            .. summary.Heatmap.Chunk(7).Select(week => new StatsHeatWeekViewModel(
            [
                .. week.Select(d => new StatsHeatCellViewModel(
                    Math.Min(d.Workouts, MaxHeatLevel),
                    d.Date > today,
                    _localizer["StatsDayName", d.Date.ToString("M", culture), d.Workouts].Value)),
            ])),
        ];

        Programs =
        [
            .. summary.Programs.Select(p => new StatsProgramViewModel(
                p.EnrollmentId,
                p.Name,
                p.CompletionRate,
                p.CompletionRate.ToString("P0", culture),
                _localizer["StatsProgramDetail", p.CrossedDays, p.ScheduledDays, p.LongestStreak].Value)),
        ];

        ShowGate = !isPro;
        ShowEmptyState = summary.TotalWorkouts == 0;
    }

    private string Duration(int minutes) =>
        minutes < 60 ? _localizer["StatsMinutes", minutes].Value
        : minutes % 60 == 0 ? _localizer["StatsHours", minutes / 60].Value
        : _localizer["StatsHoursMinutes", minutes / 60, minutes % 60].Value;

    private static double Fraction(int value, int max) => max <= 0 ? 0 : (double)value / max;

    // The culture's month-day order with a short month: "Nov 16", "16. lis".
    private static string ShortDate(DateOnly date)
    {
        var culture = CultureInfo.CurrentCulture;
        return date.ToString(culture.DateTimeFormat.MonthDayPattern.Replace("MMMM", "MMM", StringComparison.Ordinal), culture);
    }
}
