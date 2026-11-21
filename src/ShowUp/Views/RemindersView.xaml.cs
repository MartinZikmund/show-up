using ShowUp.Core.ViewModels;

namespace ShowUp.Views;

public partial class RemindersViewBase : ViewBase<RemindersViewModel> { }

/// <summary>Reached from Settings.</summary>
public sealed partial class RemindersView : RemindersViewBase
{
    public RemindersView() => InitializeComponent();
}
