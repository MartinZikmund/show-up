using ShowUp.Core.Navigation;

namespace ShowUp.Core.Services;

/// <summary>Switches the window's top-level section, keeping the tab bar or navigation pane in sync.</summary>
public interface ISectionNavigator
{
    void NavigateToSection(NavigationSection section);
}
