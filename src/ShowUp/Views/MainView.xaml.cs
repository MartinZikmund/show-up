using ShowUp.Core.Navigation;
using ShowUp.Core.ViewModels;

namespace ShowUp.Views;

public partial class MainViewBase : ViewBase<MainViewModel> { }

[NavigationInfo(NavigationSection.Main)]
public sealed partial class MainView : MainViewBase
{
    public MainView()
    {
        this.InitializeComponent();
#if DEBUG
        DebugStoragePanel.Visibility = Visibility.Visible;
#endif
    }
}
