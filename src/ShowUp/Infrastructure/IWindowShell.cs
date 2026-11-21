using ShowUp.Core.Marking;
using ShowUp.Core.ViewModels;
using Microsoft.UI.Dispatching;

namespace ShowUp.Infrastructure;

public interface IWindowShell
{
    WindowShellViewModel ViewModel { get; }

    XamlRoot? XamlRoot { get; }

    IServiceProvider ServiceProvider { get; }

    DispatcherQueue DispatcherQueue { get; }

    Frame RootFrame { get; }

    ICrossMoment CrossMoment { get; }

    void SetTitleBar(UIElement? titleBar);
}
