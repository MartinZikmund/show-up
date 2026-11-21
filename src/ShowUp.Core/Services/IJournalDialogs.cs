using ShowUp.Core.Storage;

namespace ShowUp.Core.Services;

/// <summary>The notes and results dialogs. Each completes when closed (saved or not).</summary>
public interface IJournalDialogs
{
    /// <summary>The day's sessions, notes, results and bonus workouts, with uncross.</summary>
    Task ShowDayAsync(int dayIndex);

    Task LogResultsAsync(int dayIndex, int sessionIndex);

    Task LogBonusResultsAsync(BonusWorkoutEntity bonus);

    Task EditProgramNoteAsync();
}
