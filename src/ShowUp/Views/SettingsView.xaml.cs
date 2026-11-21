using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Input;
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
        AccountErrorText.RegisterPropertyChangedCallback(VisibilityProperty, (d, _) => AnnounceIfShown(d));
        // A different error while one is already showing is news too.
        AccountErrorText.RegisterPropertyChangedCallback(TextBlock.TextProperty, (d, _) => AnnounceIfShown(d));
        Loaded += (_, _) =>
        {
            LockMarkerColorInHighContrast();
#if DEBUG
            AddProOverride();
            AddSimulateSignIn();
#endif
        };
    }

    private void EmailBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter && ViewModel.Account.SendCodeCommand.CanExecute(null))
        {
            e.Handled = true;
            ViewModel.Account.SendCodeCommand.Execute(null);
        }
    }

    private void CodeBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter && ViewModel.Account.VerifyCommand.CanExecute(null))
        {
            e.Handled = true;
            ViewModel.Account.VerifyCommand.Execute(null);
        }
    }

    // Screen readers hear a new error without having to find it.
    private static void AnnounceIfShown(DependencyObject element)
    {
        if (element is TextBlock { Visibility: Visibility.Visible } shown)
        {
            (FrameworkElementAutomationPeer.FromElement(shown) ?? FrameworkElementAutomationPeer.CreatePeerForElement(shown))
                ?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        }
    }

#if DEBUG
    private ComboBox? _proOverride;
    private ToggleSwitch? _simulateSignIn;

    // Signs out first: a simulated session must not outlive the fake server, nor a real one hide behind it.
    private void AddSimulateSignIn()
    {
        var provider = FindWindowShell(XamlRoot?.Content)?.ServiceProvider;
        if (_simulateSignIn is not null
            || provider?.GetService<ShowUp.Services.Auth.DebugSupabaseAuth>() is not { } auth
            || provider.GetService<ShowUp.Core.Accounts.AccountService>() is not { } account)
        {
            return;
        }

        _simulateSignIn = new()
        {
            Header = Localizer.Instance["DebugSimulateSignIn"],
            IsOn = auth.Simulate,
        };
        AutomationProperties.SetAutomationId(_simulateSignIn, "DebugSimulateSignInToggle");
        ToolTipService.SetToolTip(_simulateSignIn, Localizer.Instance["DebugSimulateSignInDescription"]);
        _simulateSignIn.Toggled += async (_, _) =>
        {
            if (auth.Simulate == _simulateSignIn.IsOn)
            {
                return;
            }

            try
            {
                if (account.Current is not null)
                {
                    await account.SignOutAsync();
                }

                auth.Simulate = _simulateSignIn.IsOn;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Switching the simulated sign-in failed: {ex}");
                _simulateSignIn.IsOn = auth.Simulate;
            }

            ViewModel.Account.Update();
        };
        DebugPanel.Children.Add(_simulateSignIn);
    }

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
