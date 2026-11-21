using Microsoft.Extensions.Logging;
using ShowUp.Core.Services;
using ShowUp.Core.Storage;
using ShowUp.Core.Tracking;
using ShowUp.Core.ViewModels;
using ShowUp.Views;

namespace ShowUp.Services.Dialogs;

public sealed class JournalDialogService(
    IDialogCoordinator dialogCoordinator,
    IXamlRootProvider xamlRootProvider,
    IServiceProvider services,
    IJournalService journal,
    IActiveEnrollmentService enrollments,
    ILogger<JournalDialogService> logger) : IJournalDialogs
{
    public async Task ShowDayAsync(int dayIndex)
    {
        var viewModel = services.GetRequiredService<DayDetailViewModel>();
        if (!await viewModel.LoadAsync(dayIndex))
        {
            return;
        }

        DayDetailDialog dialog = new(viewModel) { XamlRoot = xamlRootProvider.XamlRoot };
        if (await dialogCoordinator.ShowAsync(dialog) == ContentDialogResult.Primary)
        {
            await SaveAsync(viewModel.SaveAsync);
        }
    }

    public async Task LogResultsAsync(int dayIndex, int sessionIndex)
    {
        var viewModel = services.GetRequiredService<LogResultsViewModel>();
        if (await viewModel.LoadForSessionAsync(dayIndex, sessionIndex))
        {
            await ShowResultsAsync(viewModel);
        }
    }

    public async Task LogBonusResultsAsync(BonusWorkoutEntity bonus)
    {
        var viewModel = services.GetRequiredService<LogResultsViewModel>();
        if (await viewModel.LoadForBonusAsync(bonus))
        {
            await ShowResultsAsync(viewModel);
        }
    }

    public async Task EditProgramNoteAsync()
    {
        if (await enrollments.GetAsync() is not { } active)
        {
            return;
        }

        ProgramNoteDialog dialog = new(active.Program.Name, await journal.GetProgramNoteAsync()) { XamlRoot = xamlRootProvider.XamlRoot };
        if (await dialogCoordinator.ShowAsync(dialog) == ContentDialogResult.Primary)
        {
            await SaveAsync(() => journal.SaveProgramNoteAsync(dialog.Note));
        }
    }

    private async Task ShowResultsAsync(LogResultsViewModel viewModel)
    {
        LogResultsDialog dialog = new(viewModel) { XamlRoot = xamlRootProvider.XamlRoot };
        if (await dialogCoordinator.ShowAsync(dialog) == ContentDialogResult.Primary)
        {
            await SaveAsync(viewModel.SaveAsync);
        }
    }

    // The enrollment can end while a dialog is open; losing the edit beats crashing.
    private async Task SaveAsync(Func<Task> save)
    {
        try
        {
            await save();
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(ex, "Saving notes or results failed.");
        }
    }
}
