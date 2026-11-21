using System.Globalization;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging;
using ShowUp.Core.Messages;
using ShowUp.Core.Services;
using ShowUp.Core.Tracking;
using ShowUp.Services.Settings;

namespace ShowUp.Core.ViewModels;

public enum ResultsTrend
{
    None,
    Same,
    Better,
    Worse,
}

public sealed record ResultsColumnHeader(string Label, string DateLabel);

public sealed record ResultsCellViewModel(string ValueText, string DeltaText, ResultsTrend Trend, bool IsUp, string AutomationName)
{
    public bool HasDelta => DeltaText.Length > 0;

    public bool IsBetter => HasDelta && Trend == ResultsTrend.Better;

    public bool IsWorse => HasDelta && Trend == ResultsTrend.Worse;

    public bool IsSame => HasDelta && Trend is not (ResultsTrend.Better or ResultsTrend.Worse);

    public string ArrowGlyph => IsUp ? "\uE70E" : "\uE70D";
}

public sealed record ResultsRowViewModel(string MetricName, string UnitHint, IReadOnlyList<ResultsCellViewModel> Cells);

/// <summary>A workout with nothing logged yet, shown as one line instead of an empty table.</summary>
public sealed record PendingResultsViewModel(string Title, string Detail, string AutomationId);

public sealed record ResultsTableViewModel(string Title, string AutomationId, IReadOnlyList<ResultsColumnHeader> Columns, IReadOnlyList<ResultsRowViewModel> Rows);

/// <summary>Every Fit Test (and other workout with metrics) of the selected program side by side, with the change against the first one.</summary>
public partial class ProgramResultsViewModel : ViewModelBase
{
    private readonly IJournalService _journal;
    private readonly IActiveEnrollmentService _enrollments;
    private readonly IJournalDialogs _dialogs;
    private readonly INavigationService _navigation;
    private readonly IMessenger _messenger;
    private readonly IStringLocalizer _localizer;
    private readonly IAppPreferences _preferences;
    private readonly TimeProvider _time;
    private readonly ILogger<ProgramResultsViewModel> _logger;
    private readonly SemaphoreSlim _loadGate = new(1, 1);
    private SynchronizationContext? _context;
    private bool _isRegistered;
    private bool _syncingSelection;
    private Guid? _enrollmentId;

    public ProgramResultsViewModel(
        IJournalService journal,
        IActiveEnrollmentService enrollments,
        IJournalDialogs dialogs,
        INavigationService navigation,
        IMessenger messenger,
        IStringLocalizer localizer,
        IAppPreferences preferences,
        TimeProvider time,
        ILogger<ProgramResultsViewModel> logger)
    {
        _journal = journal;
        _enrollments = enrollments;
        _dialogs = dialogs;
        _navigation = navigation;
        _messenger = messenger;
        _localizer = localizer;
        _preferences = preferences;
        _time = time;
        _logger = logger;
        PageTitle = localizer["ResultsTitle"];
    }

    [ObservableProperty]
    public partial IReadOnlyList<ResultsTableViewModel> Tables { get; private set; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPending))]
    public partial IReadOnlyList<PendingResultsViewModel> Pending { get; private set; } = [];

    public bool HasPending => Pending.Count > 0;

    [ObservableProperty]
    public partial string ProgramName { get; private set; } = "";

    [ObservableProperty]
    public partial bool HasEnrollment { get; private set; }

    /// <summary>Every active program, for the switcher.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowProgramSwitcher))]
    public partial IReadOnlyList<ProgramOption> Programs { get; private set; } = [];

    [ObservableProperty]
    public partial ProgramOption? SelectedProgram { get; set; }

    public bool ShowProgramSwitcher => Programs.Count > 1;

    [ObservableProperty]
    public partial bool ShowEmptyState { get; private set; }

    public override void ViewLoaded()
    {
        base.ViewLoaded();
        _context = SynchronizationContext.Current;
        if (!_isRegistered)
        {
            _isRegistered = true;
            Register();
        }

        RunInBackground(LoadAsync);
    }

    private void Register()
    {
        _messenger.Register<ProgramResultsViewModel, JournalChangedMessage>(this, static (r, _) => r.Post(() => r.RunInBackground(r.LoadAsync)));
        _messenger.Register<ProgramResultsViewModel, TrackingChangedMessage>(this, static (r, _) => r.Post(() => r.RunInBackground(r.LoadAsync)));
    }

    public override void ViewUnloaded()
    {
        base.ViewUnloaded();
        _isRegistered = false;
        _messenger.UnregisterAll(this);
    }

    public async Task LoadAsync()
    {
        await _loadGate.WaitAsync();
        try
        {
            await LoadCoreAsync();
        }
        finally
        {
            _loadGate.Release();
        }
    }

    private async Task LoadCoreAsync()
    {
        var all = await _enrollments.GetAllAsync();
        var active = await _enrollments.GetSelectedAsync();
        _enrollmentId = active?.Id;
        HasEnrollment = active is not null;
        ProgramName = active?.Program.Name ?? "";
        SyncPrograms(all, active);
        var today = DateOnly.FromDateTime(_time.GetLocalNow().DateTime);
        IReadOnlyList<ResultsTable> tables = active is null ? [] : await _journal.GetResultsAsync(active.Id);
        Tables = [.. tables.Where(t => t.HasValues).Select(t => ToViewModel(t.UpToNext(today)))];
        Pending = [.. tables.Where(t => !t.HasValues).Select(t => ToPending(t, today))];
        ShowEmptyState = tables.Count == 0;
    }

    [RelayCommand]
    private void GoBack() => _navigation.GoBack();

    [RelayCommand]
    private Task EditProgramNoteAsync() => _enrollmentId is { } id ? _dialogs.EditProgramNoteAsync(id) : Task.CompletedTask;

    /// <summary>Shows another active program's results; the choice is remembered (and synced).</summary>
    public async Task SelectProgramAsync(ProgramOption? option)
    {
        if (option is null || option.Id == _enrollmentId)
        {
            return;
        }

        try
        {
            await _enrollments.SelectAsync(option.Id);
        }
        catch (InvalidOperationException)
        {
            // It ended under us; the reload shows what's true now.
        }

        await LoadAsync();
    }

    partial void OnSelectedProgramChanged(ProgramOption? value)
    {
        if (!_syncingSelection)
        {
            RunInBackground(() => SelectProgramAsync(value));
        }
    }

    private void SyncPrograms(IReadOnlyList<ActiveEnrollment> all, ActiveEnrollment? selected)
    {
        _syncingSelection = true;
        try
        {
            List<ProgramOption> options = [.. all.Select(e => new ProgramOption(e.Id, e.Program.Name))];
            if (!Programs.SequenceEqual(options))
            {
                Programs = options;
            }

            SelectedProgram = Programs.FirstOrDefault(o => o.Id == selected?.Id);
        }
        finally
        {
            _syncingSelection = false;
        }
    }

    private PendingResultsViewModel ToPending(ResultsTable table, DateOnly today) => new(
        table.Workout.Name,
        table.NextScheduled(today) is { DayIndex: { } day }
            ? _localizer["ResultsNotLoggedNextFormat", day + 1].Value
            : _localizer["ResultsNotLogged"].Value,
        $"ResultsPending_{table.Workout.Id}");

    private ResultsTableViewModel ToViewModel(ResultsTable table)
    {
        var dayFormat = _localizer["CalendarDayNumberFormat"].Value;
        var bonus = _localizer["ResultsBonusColumn"].Value;
        List<ResultsColumnHeader> columns =
        [
            .. table.Columns.Select(c => new ResultsColumnHeader(
                c.DayIndex is { } d ? string.Format(CultureInfo.CurrentCulture, dayFormat, d + 1) : bonus,
                c.Date.ToString("d MMM", CultureInfo.CurrentCulture))),
        ];

        List<ResultsRowViewModel> rows = [];
        foreach (var row in table.Rows)
        {
            var unit = row.Metric.Unit;
            var system = _preferences.UnitSystem;
            var hint = MetricUnits.Label(unit, system, _localizer);
            List<ResultsCellViewModel> cells = [];
            for (var i = 0; i < row.Cells.Count; i++)
            {
                var cell = row.Cells[i];
                var value = cell.Value is { } v ? MetricFormat.Value(unit, v, system) : "";
                var delta = cell.Delta is { } dd ? MetricFormat.Delta(unit, dd, system) : "";
                var trend = cell.Value is null ? ResultsTrend.None
                    : cell.Improved switch { true => ResultsTrend.Better, false => ResultsTrend.Worse, null => cell.Delta is null ? ResultsTrend.None : ResultsTrend.Same };
                var spoken = hint.Length == 0 ? value : $"{value} {hint}";
                var name = value.Length == 0
                    ? _localizer["ResultsCellEmptyFormat", row.Metric.Name, columns[i].Label].Value
                    : delta.Length == 0
                        ? _localizer["ResultsCellFormat", row.Metric.Name, columns[i].Label, spoken].Value
                        : _localizer["ResultsCellDeltaFormat", row.Metric.Name, columns[i].Label, spoken, delta].Value;
                cells.Add(new ResultsCellViewModel(value, delta, trend, cell.Delta > 0, name));
            }

            rows.Add(new ResultsRowViewModel(row.Metric.Name, hint, cells));
        }

        return new ResultsTableViewModel(table.Workout.Name, $"ResultsTable_{table.Workout.Id}", columns, rows);
    }

    private async void RunInBackground(Func<Task> work)
    {
        try
        {
            await work();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Loading the results failed.");
        }
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
}
