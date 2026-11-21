using ShowUp.Core.Navigation;
using ShowUp.Core.ViewModels;

namespace ShowUp.Views;

public partial class TodayViewBase : ViewBase<TodayViewModel> { }

[NavigationInfo(NavigationSection.Today)]
public sealed partial class TodayView : TodayViewBase
{
    public TodayView() => InitializeComponent();
}
