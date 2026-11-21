using ShowUp.Core.Navigation;
using ShowUp.Core.ViewModels;

namespace ShowUp.Views;

public partial class ProgramCalendarViewBase : ViewBase<ProgramCalendarViewModel> { }

[NavigationInfo(NavigationSection.Calendar)]
public sealed partial class ProgramCalendarView : ProgramCalendarViewBase
{
    public ProgramCalendarView() => InitializeComponent();
}
