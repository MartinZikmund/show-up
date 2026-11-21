using ShowUp.Core.Navigation;

namespace ShowUp.Core.Services;

public interface INavigationService
{
    bool CanGoBack { get; }

    NavigationSection? CurrentSection { get; }

    void Initialize();

    void Navigate<TViewModel>();

    void Navigate<TViewModel>(object? parameter);

    bool GoBack();

    void ClearBackStack();
}
