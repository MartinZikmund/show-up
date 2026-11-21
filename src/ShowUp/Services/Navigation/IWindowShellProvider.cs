using ShowUp.Core.ViewModels;
using ShowUp.Infrastructure;
using Microsoft.UI.Dispatching;

namespace ShowUp.Services.Navigation;

public interface IWindowShellProvider
{
    Window Window { get; }

    IWindowShell Shell { get; }

    WindowShellViewModel ViewModel { get; }

    DispatcherQueue DispatcherQueue { get; }

    Frame RootFrame { get; }
}
