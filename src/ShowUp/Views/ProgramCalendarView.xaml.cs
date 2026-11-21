using ShowUp.Core.Navigation;
using ShowUp.Core.ViewModels;

namespace ShowUp.Views;

public partial class ProgramCalendarViewBase : ViewBase<ProgramCalendarViewModel> { }

[NavigationInfo(NavigationSection.Calendar)]
public sealed partial class ProgramCalendarView : ProgramCalendarViewBase
{
    private Window? _window;

    public ProgramCalendarView()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            Bindings.Update();
            _window = FindWindowShell(XamlRoot?.Content)?.ServiceProvider.GetRequiredService<IWindowShellProvider>().Window;
            if (_window is not null)
            {
                _window.Activated += Window_Activated;
            }
        };
        Unloaded += (_, _) =>
        {
            if (_window is not null)
            {
                _window.Activated -= Window_Activated;
                _window = null;
            }
        };
    }

    // Coming back to the app the next morning moves today on.
    private void Window_Activated(object sender, WindowActivatedEventArgs args)
    {
        if (args.WindowActivationState != WindowActivationState.Deactivated && ViewModel is { } viewModel)
        {
            _ = viewModel.RefreshIfDateChangedAsync();
        }
    }
}
