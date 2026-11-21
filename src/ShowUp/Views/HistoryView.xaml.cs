using ShowUp.Core.Navigation;
using ShowUp.Core.ViewModels;

namespace ShowUp.Views;

public partial class HistoryViewBase : ViewBase<HistoryViewModel> { }

[NavigationInfo(NavigationSection.History)]
public sealed partial class HistoryView : HistoryViewBase
{
    public HistoryView() => InitializeComponent();
}
