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
            AddProOverride();
#endif
        };
    }

#if DEBUG
    private ComboBox? _proOverride;

    private void AddProOverride()
    {
        if (_proOverride is not null
            || FindWindowShell(XamlRoot?.Content)?.ServiceProvider.GetService<ShowUp.Services.Entitlements.DebugEntitlementOverride>() is not { } entitlements)
        {
            return;
        }

        _proOverride = new()
        {
            Header = Localizer.Instance["DebugProOverride"],
            ItemsSource = new[] { Localizer.Instance["DebugProOverrideReal"], Localizer.Instance["DebugProOverrideForcePro"], Localizer.Instance["DebugProOverrideForceFree"] },
            SelectedIndex = (int)entitlements.Override,
        };
        AutomationProperties.SetAutomationId(_proOverride, "DebugProOverrideCombo");
        _proOverride.SelectionChanged += (_, _) =>
        {
            if (_proOverride.SelectedIndex >= 0)
            {
                entitlements.Override = (ShowUp.Services.Entitlements.ProOverride)_proOverride.SelectedIndex;
            }
        };
        DebugPanel.Children.Add(_proOverride);
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
