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
        AddDebugStoragePanel();
#endif
    }

#if DEBUG
    private void AddDebugStoragePanel()
    {
        TextBlock count = new() { HorizontalAlignment = HorizontalAlignment.Center };
        AutomationProperties.SetAutomationId(count, "BonusRowCountText");
        count.SetBinding(TextBlock.TextProperty, new Binding { Path = new PropertyPath(nameof(MainViewModel.BonusRowCount)) });

        Button add = new() { HorizontalAlignment = HorizontalAlignment.Center, Content = "Add row" };
        AutomationProperties.SetAutomationId(add, "AddBonusRowButton");
        add.SetBinding(ButtonBase.CommandProperty, new Binding { Path = new PropertyPath(nameof(MainViewModel.AddBonusRowCommand)) });

        ContentPanel.Children.Add(count);
        ContentPanel.Children.Add(add);
    }
#endif
}
