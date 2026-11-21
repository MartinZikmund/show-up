using ShowUp.Core.ViewModels;
using ShowUp.Services.Localization;

namespace ShowUp.Views;

/// <summary>A day's sheet: full height on phones, a ~480 px dialog when wider.</summary>
public sealed partial class DayDetailDialog : ContentDialog
{
    public DayDetailDialog(DayDetailViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        JournalDialogLayout.Apply(this, ContentPanel);
    }

    public DayDetailViewModel ViewModel { get; }

    public static int StrokesFor(bool isDone) => isDone ? 2 : 0;

    public static string StatusText(bool isDone) => Localizer.Instance[isDone ? "DayDetailSessionDone" : "DayDetailSessionNotDone"];

    // The flyout lives outside the template's visual tree, so the session rides along on Tag.
    private void ConfirmUncross_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).Tag is DaySessionViewModel session)
        {
            ViewModel.UncrossCommand.Execute(session);
        }
    }
}
