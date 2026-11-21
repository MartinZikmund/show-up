using System.Globalization;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging;
using ShowUp.Core.Messages;
using ShowUp.Core.Services;
using ShowUp.Core.Tracking;

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

public sealed record ResultsTableViewModel(string Title, string AutomationId, IReadOnlyList<ResultsColumnHeader> Columns, IReadOnlyList<ResultsRowViewModel> Rows);

/// <summary>Every Fit Test (and other workout with metrics) of the active program side by side, with the change against the first one.</summary>
public partial class ProgramResultsViewModel : ViewModelBase
{
    private readonly IJournalService _journal;
    private readonly IActiveEnrollmentService _enrollments;
    private readonly IJournalDialogs _dialogs;
    private readonly INavigationService _navigation;
    private readonly IMessenger _messenger;
    private readonly IStringLocalizer _localizer;
    private readonly ILogger<ProgramResultsViewModel> _logger;
    private SynchronizationContext? _context;

    public ProgramResultsViewModel(
        IJournalService journal,
        IActiveEnrollmentService enrollments,
        IJournalDialogs dialogs,
        INavigationService navigation,
        IMessenger messenger,
        IStringLocalizer localizer,
        ILogger<ProgramResultsViewModel> logger)
    {
        _journal = journal;
        _enrollments = enrollments;
        _dialogs = dialogs;
        _navigation = navigation;
        _messenger = messenger;
        _localizer = localizer;
        _logger = logger;
        PageTitle = localizer["ResultsTitle"];
    }

    [ObservableProperty]
    public partial IReadOnlyList<ResultsTableViewModel> Tables { get; private set; } = [];

    [ObservableProperty]
    public partial string ProgramName { get; private set; } = "";

    [ObservableProperty]
    public partial bool HasEnrollment { get; private set; }

    [ObservableProperty]
    public partial bool ShowEmptyState { get; private set; }

    public override void ViewLoaded()
    {
        base.ViewLoaded();
        _context = SynchronizationContext.Current;
        _messenger.Register<ProgramResultsViewModel, JournalChangedMessage>(this, static (r, _) => r.Post(() => r.RunInBackground(r.LoadAsync)));
        _messenger.Register<ProgramResultsViewModel, TrackingChangedMessage>(this, static (r, _) => r.Post(() => r.RunInBackground(r.LoadAsync)));
        RunInBackground(LoadAsync);
    }

    public override void ViewUnloaded()
    {
        base.ViewUnloaded();
        _messenger.UnregisterAll(this);
    }

    public async Task LoadAsync()
    {
        var active = await _enrollments.GetAsync();
        HasEnrollment = active is not null;
        ProgramName = active?.Program.Name ?? "";
        var tables = await _journal.GetResultsAsync();
        Tables = [.. tables.Select(ToViewModel)];
        ShowEmptyState = Tables.Count == 0;
    }

    [RelayCommand]
    private void GoBack() => _navigation.GoBack();

    [RelayCommand]
    private Task EditProgramNoteAsync() => _dialogs.EditProgramNoteAsync();

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
            var hint = new MetricInputViewModel(row.Metric, null, _localizer, MetricInputViewModel.UsesMetricSystem).UnitHint;
            List<ResultsCellViewModel> cells = [];
            for (var i = 0; i < row.Cells.Count; i++)
            {
                var cell = row.Cells[i];
                var value = cell.Value is { } v ? MetricFormat.Value(unit, v) : "";
                var delta = cell.Delta is { } dd ? MetricFormat.Delta(unit, dd) : "";
                var trend = cell.Value is null ? ResultsTrend.None
                    : cell.Improved switch { true => ResultsTrend.Better, false => ResultsTrend.Worse, null => cell.Delta is null ? ResultsTrend.None : ResultsTrend.Same };
                var name = value.Length == 0
                    ? _localizer["ResultsCellEmptyFormat", row.Metric.Name, columns[i].Label].Value
                    : delta.Length == 0
                        ? _localizer["ResultsCellFormat", row.Metric.Name, columns[i].Label, value].Value
                        : _localizer["ResultsCellDeltaFormat", row.Metric.Name, columns[i].Label, value, delta].Value;
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
