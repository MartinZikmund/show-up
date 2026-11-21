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
        Loaded += (_, _) => LockMarkerColorInHighContrast();
    }

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
