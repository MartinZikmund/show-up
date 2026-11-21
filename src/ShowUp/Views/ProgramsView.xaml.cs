using ShowUp.Core.Navigation;
using ShowUp.Core.ViewModels;

namespace ShowUp.Views;

public partial class ProgramsViewBase : ViewBase<ProgramsViewModel> { }

[NavigationInfo(NavigationSection.Programs)]
public sealed partial class ProgramsView : ProgramsViewBase
{
    public ProgramsView() => InitializeComponent();
}
