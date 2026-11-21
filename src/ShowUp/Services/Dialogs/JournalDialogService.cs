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
    JournalSaveGuard saveGuard) : IJournalDialogs
{
    public async Task ShowDayAsync(Guid enrollmentId, int dayIndex)
    {
        var viewModel = services.GetRequiredService<DayDetailViewModel>();
        if (!await viewModel.LoadAsync(enrollmentId, dayIndex))
        {
            return;
        }

        DayDetailDialog dialog = new(viewModel) { XamlRoot = xamlRootProvider.XamlRoot };
        if (await dialogCoordinator.ShowAsync(dialog) == ContentDialogResult.Primary)
        {
            await SaveAsync(viewModel.SaveAsync);
        }
    }

    public async Task LogResultsAsync(Guid enrollmentId, int dayIndex, int sessionIndex)
    {
        var viewModel = services.GetRequiredService<LogResultsViewModel>();
        if (await viewModel.LoadForSessionAsync(enrollmentId, dayIndex, sessionIndex))
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

    public async Task EditProgramNoteAsync(Guid enrollmentId)
    {
        if (await enrollments.GetAsync(enrollmentId) is not { } active)
        {
            return;
        }

        ProgramNoteDialog dialog = new(active.Program.Name, await journal.GetProgramNoteAsync(enrollmentId), enrollments.IsReadOnly(active))
        {
            XamlRoot = xamlRootProvider.XamlRoot,
        };
        if (await dialogCoordinator.ShowAsync(dialog) == ContentDialogResult.Primary)
        {
            await SaveAsync(() => journal.SaveProgramNoteAsync(enrollmentId, dialog.Note));
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

    private Task SaveAsync(Func<Task> save) => saveGuard.RunAsync(save);
}
