using ShowUp.Core.Catalog;
using ShowUp.Core.Services;
using ShowUp.Core.ViewModels;
using ShowUp.Views;

namespace ShowUp.Services.Dialogs;

public sealed class BonusWorkoutDialogService(
    IDialogCoordinator dialogCoordinator,
    IXamlRootProvider xamlRootProvider,
    ICatalogService catalog,
    TimeProvider time) : IBonusWorkoutDialog
{
    public async Task<BonusWorkoutEntry?> ShowAsync(DateOnly date)
    {
        var today = DateOnly.FromDateTime(time.GetLocalNow().DateTime);
        BonusWorkoutDialogViewModel viewModel = new((await catalog.GetCatalogAsync()).Workouts, today, date);
        var xamlRoot = xamlRootProvider.XamlRoot;
        BonusWorkoutDialog dialog = new(viewModel) { XamlRoot = xamlRoot };
        // Dialogs follow the system theme, not the app's.
        if (xamlRoot?.Content is FrameworkElement root)
        {
            dialog.RequestedTheme = root.ActualTheme;
        }

        var result = await dialogCoordinator.ShowAsync(dialog);
        return result == ContentDialogResult.Primary && viewModel.CanSave ? viewModel.ToEntry() : null;
    }
}
