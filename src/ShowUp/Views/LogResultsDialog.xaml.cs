using ShowUp.Core.ViewModels;

namespace ShowUp.Views;

public sealed partial class LogResultsDialog : ContentDialog
{
    public LogResultsDialog(LogResultsViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        JournalDialogLayout.Apply(this, ContentPanel);
    }

    public LogResultsViewModel ViewModel { get; }
}
