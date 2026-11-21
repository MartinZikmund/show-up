using ShowUp.Core.Navigation;
using ShowUp.Core.ViewModels;
using ShowUp.Services.Localization;
using Windows.UI.ViewManagement;

namespace ShowUp.Views;

public partial class SettingsViewBase : ViewBase<SettingsViewModel> { }

[NavigationInfo(NavigationSection.Settings)]
public sealed partial class SettingsView : SettingsViewBase
{
    public SettingsView()
    {
        this.InitializeComponent();
        Loaded += (_, _) =>
        {
            LockMarkerColorInHighContrast();
#if DEBUG
            AddSimulateFreeToggle();
#endif
        };
    }

#if DEBUG
    private ToggleSwitch? _simulateFree;

    private void AddSimulateFreeToggle()
    {
        if (_simulateFree is not null
            || FindWindowShell(XamlRoot?.Content)?.ServiceProvider.GetService<ShowUp.Services.Entitlements.DebugEntitlementOverride>() is not { } entitlements)
        {
            return;
        }

        _simulateFree = new() { Header = Localizer.Instance["DebugSimulateFree"], IsOn = entitlements.SimulateFree };
        AutomationProperties.SetAutomationId(_simulateFree, "DebugSimulateFreeToggle");
        _simulateFree.Toggled += (_, _) => entitlements.SimulateFree = _simulateFree.IsOn;
        DebugPanel.Children.Add(_simulateFree);
    }
#endif

    // System High Contrast colors win over the marker color, so say so instead of offering a dead picker.
    private void LockMarkerColorInHighContrast()
    {
        try
        {
            if (new AccessibilitySettings().HighContrast)
            {
                MarkerColorPicker.IsEnabled = false;
                MarkerColorCard.Description = Localizer.Instance["MarkerColorHighContrastNote"];
            }
        }
        catch
        {
            // Heads without AccessibilitySettings are never in High Contrast.
        }
    }
}
