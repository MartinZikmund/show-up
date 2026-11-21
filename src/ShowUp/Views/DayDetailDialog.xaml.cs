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
        if (viewModel.IsReadOnly)
        {
            // Nothing to save: a lapsed program is shown, not edited.
            PrimaryButtonText = "";
            CloseButtonText = Localizer.Instance["Close"];
        }
    }

    public DayDetailViewModel ViewModel { get; }

    public static int StrokesFor(bool isDone) => isDone ? 2 : 0;

    public static string StatusText(bool isDone, bool pendingUncross) =>
        Localizer.Instance[pendingUncross ? "DayDetailUncrossPending" : isDone ? "DayDetailSessionDone" : "DayDetailSessionNotDone"];

    private void ToggleUncross_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).Tag is DaySessionViewModel session)
        {
            ViewModel.ToggleUncrossCommand.Execute(session);
        }
    }
}
