using ShowUp.Core.Navigation;
using ShowUp.Core.ViewModels;

namespace ShowUp.Views;

public partial class ProgramsViewBase : ViewBase<ProgramsViewModel> { }

[NavigationInfo(NavigationSection.Programs)]
public sealed partial class ProgramsView : ProgramsViewBase
{
    public ProgramsView() => InitializeComponent();

    public static Visibility Not(bool value) => value ? Visibility.Collapsed : Visibility.Visible;

    private void Card_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: ProgramCardViewModel card })
        {
            ViewModel?.OpenCommand.Execute(card);
        }
    }
}
