using ShowUp.Controls;
using ShowUp.Core.Navigation;
using ShowUp.Core.ViewModels;

namespace ShowUp.Views;

public partial class HistoryViewBase : ViewBase<HistoryViewModel> { }

[NavigationInfo(NavigationSection.History)]
public sealed partial class HistoryView : HistoryViewBase
{
    public HistoryView() => InitializeComponent();

    public static Brush AccentBrush(string accentColor) => new SolidColorBrush(ProgramHero.ParseColor(accentColor));

    private void Calendar_Click(object sender, RoutedEventArgs e) =>
        ViewModel.OpenCalendarCommand.Execute(((FrameworkElement)sender).Tag as HistoryProgramViewModel);

    private void Results_Click(object sender, RoutedEventArgs e) =>
        ViewModel.OpenResultsCommand.Execute(((FrameworkElement)sender).Tag as HistoryProgramViewModel);
}
