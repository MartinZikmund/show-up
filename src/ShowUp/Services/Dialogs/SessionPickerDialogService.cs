using ShowUp.Core.Catalog;
using ShowUp.Core.Services;
using ShowUp.Core.ViewModels;
using ShowUp.Views;

namespace ShowUp.Services.Dialogs;

public sealed class SessionPickerDialogService(IDialogCoordinator dialogCoordinator, IXamlRootProvider xamlRootProvider) : ISessionPickerDialog
{
    public async Task<SessionPick?> ShowAsync(IReadOnlyList<Workout> workouts)
    {
        SessionPickerViewModel viewModel = new(workouts);
        SessionPickerDialog dialog = new(viewModel) { XamlRoot = xamlRootProvider.XamlRoot };

        var result = await dialogCoordinator.ShowAsync(dialog);
        return result == ContentDialogResult.Primary && viewModel.CanSave ? viewModel.ToPick() : null;
    }
}
