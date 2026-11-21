using System.Globalization;
using Microsoft.Extensions.Logging;
using ShowUp.Core.Storage;
using ShowUp.Core.Tracking;

namespace ShowUp.Core.ViewModels;

public sealed partial class DaySessionViewModel : ObservableObject
{
    private readonly string _initialNote;

    internal DaySessionViewModel(SessionJournal session, ResultsEditorViewModel? results, string optionalFormat)
    {
        Index = session.SessionIndex;
        Name = session.IsOptional ? string.Format(CultureInfo.CurrentCulture, optionalFormat, session.Name) : session.Name;
        IsDone = session.IsDone;
        Note = _initialNote = session.Note;
        Results = results;
    }

    public int Index { get; }

    public string Name { get; }

    public ResultsEditorViewModel? Results { get; }

    public IReadOnlyList<MetricInputViewModel> Inputs => Results?.Inputs ?? [];

    public bool HasResults => Results is not null;

    public string NoteAutomationId => $"DayDetailSessionNote_{Index}";

    public string UncrossAutomationId => $"DayDetailUncross_{Index}";

    [ObservableProperty]
    public partial bool IsDone { get; internal set; }

    [ObservableProperty]
    public partial string Note { get; set; }

    internal bool NoteChanged => Note.Trim() != _initialNote;
}

public sealed class DayBonusViewModel(BonusWorkoutEntity bonus, ResultsEditorViewModel? results)
{
    public BonusWorkoutEntity Bonus { get; } = bonus;

    public string Title => Bonus.Title;

    public ResultsEditorViewModel? Results { get; } = results;

    public IReadOnlyList<MetricInputViewModel> Inputs => Results?.Inputs ?? [];

    public bool HasResults => Results is not null;
}

/// <summary>One day of the calendar: its sessions with notes and results, the day's note and its bonus workouts. Saved on <see cref="SaveAsync"/>.</summary>
public sealed partial class DayDetailViewModel(
    IJournalService journal,
    IActiveEnrollmentService enrollments,
    IStringLocalizer localizer,
    ILogger<DayDetailViewModel> logger) : ObservableObject
{
    private string _initialDayNote = "";

    public int DayIndex { get; private set; } = -1;

    [ObservableProperty]
    public partial string Title { get; private set; } = "";

    [ObservableProperty]
    public partial string DateLabel { get; private set; } = "";

    [ObservableProperty]
    public partial IReadOnlyList<DaySessionViewModel> Sessions { get; private set; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasBonuses))]
    public partial IReadOnlyList<DayBonusViewModel> Bonuses { get; private set; } = [];

    [ObservableProperty]
    public partial string DayNote { get; set; } = "";

    [ObservableProperty]
    public partial bool CanSave { get; private set; }

    public bool HasBonuses => Bonuses.Count > 0;

    /// <returns>False when there's no enrollment or no such day.</returns>
    public async Task<bool> LoadAsync(int dayIndex)
    {
        if (await journal.GetDayAsync(dayIndex) is not { } day)
        {
            return false;
        }

        var metric = MetricInputViewModel.UsesMetricSystem;
        DayIndex = dayIndex;
        Title = localizer["CalendarDayNumberFormat", dayIndex + 1];
        DateLabel = day.Date.ToString("D", CultureInfo.CurrentCulture);
        var optional = localizer["ProgramOptionalFormat"].Value;
        Sessions = [.. day.Sessions.Select(s => new DaySessionViewModel(s, s.Results is { } r ? new ResultsEditorViewModel(r, localizer, metric) : null, optional))];
        Bonuses = [.. day.Bonuses.Select(b => new DayBonusViewModel(b.Bonus, b.Results is { } r ? new ResultsEditorViewModel(r, localizer, metric) : null))];
        DayNote = _initialDayNote = day.Note;

        foreach (var input in Editors().SelectMany(e => e.Inputs))
        {
            input.PropertyChanged += (_, _) => UpdateCanSave();
        }

        UpdateCanSave();
        return true;
    }

    /// <summary>Uncrossing is immediate (the sheet's edits stay pending until Save).</summary>
    [RelayCommand]
    private async Task UncrossAsync(DaySessionViewModel? session)
    {
        if (session is not { IsDone: true } || DayIndex < 0)
        {
            return;
        }

        try
        {
            await enrollments.UncrossAsync(DayIndex, session.Index);
            session.IsDone = false;
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(ex, "Uncrossing day {Day} session {Session} failed.", DayIndex, session.Index);
        }
    }

    public async Task SaveAsync()
    {
        if (DayIndex < 0 || !CanSave)
        {
            return;
        }

        if (DayNote.Trim() != _initialDayNote)
        {
            await journal.SaveNoteAsync(DayIndex, null, DayNote);
        }

        foreach (var session in Sessions.Where(s => s.NoteChanged))
        {
            await journal.SaveNoteAsync(DayIndex, session.Index, session.Note);
        }

        foreach (var editor in Editors())
        {
            if (editor.Changes is { Count: > 0 } changes)
            {
                await journal.SaveResultsAsync(editor.Sheet.Target, changes);
            }
        }
    }

    private IEnumerable<ResultsEditorViewModel> Editors() =>
        Sessions.Select(s => s.Results).Concat(Bonuses.Select(b => b.Results)).OfType<ResultsEditorViewModel>();

    private void UpdateCanSave() => CanSave = Editors().All(e => e.IsValid);
}
