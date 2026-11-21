using System.ComponentModel;
using System.Globalization;
using ShowUp.Core.Storage;
using ShowUp.Core.Tracking;
using ShowUp.Services.Settings;

namespace ShowUp.Core.ViewModels;

/// <summary>"Log your results" for one crossed session or bonus workout; saved only on <see cref="SaveAsync"/>.</summary>
public sealed partial class LogResultsViewModel(IJournalService journal, IStringLocalizer localizer, IAppPreferences preferences) : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Inputs), nameof(HasResults))]
    public partial ResultsEditorViewModel? Editor { get; private set; }

    [ObservableProperty]
    public partial string Title { get; private set; } = "";

    [ObservableProperty]
    public partial string Subtitle { get; private set; } = "";

    [ObservableProperty]
    public partial bool CanSave { get; private set; }

    public IReadOnlyList<MetricInputViewModel> Inputs => Editor?.Inputs ?? [];

    public bool HasResults => Editor is not null;

    /// <returns>False when the session's workout has nothing to log.</returns>
    public async Task<bool> LoadForSessionAsync(int dayIndex, int sessionIndex) => Load(await journal.GetResultsSheetAsync(dayIndex, sessionIndex));

    public async Task<bool> LoadForBonusAsync(BonusWorkoutEntity bonus) => Load(await journal.GetBonusResultsSheetAsync(bonus));

    public async Task SaveAsync()
    {
        if (Editor is not { IsValid: true } editor)
        {
            return;
        }

        var changes = editor.Changes;
        if (changes.Count > 0)
        {
            await journal.SaveResultsAsync(editor.Sheet.Target, changes);
        }
    }

    private bool Load(ResultsSheet? sheet)
    {
        if (Editor is not null)
        {
            foreach (var input in Editor.Inputs)
            {
                input.PropertyChanged -= Input_PropertyChanged;
            }
        }

        Editor = sheet is null ? null : new ResultsEditorViewModel(sheet, localizer, preferences.UnitSystem);
        if (Editor is null || sheet is null)
        {
            CanSave = false;
            return false;
        }

        foreach (var input in Editor.Inputs)
        {
            input.PropertyChanged += Input_PropertyChanged;
        }

        Title = sheet.Workout.Name;
        var date = sheet.Date.ToString("d", CultureInfo.CurrentCulture);
        Subtitle = sheet.DayNumber is { } day ? localizer["ResultsDayDateFormat", day, date] : localizer["ResultsBonusDateFormat", date];
        CanSave = Editor.IsValid;
        return true;
    }

    private void Input_PropertyChanged(object? sender, PropertyChangedEventArgs e) => CanSave = Editor?.IsValid == true;
}
