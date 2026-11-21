using Microsoft.UI.Xaml.Automation.Peers;
using ShowUp.Core.ViewModels;

namespace ShowUp.Controls;

/// <summary>A workout's metric inputs: a NumberBox per number, a TextBox per mm:ss time or text, each with its unit.</summary>
public sealed partial class MetricInputsControl : UserControl
{
    public static readonly DependencyProperty InputsProperty = DependencyProperty.Register(
        nameof(Inputs), typeof(IReadOnlyList<MetricInputViewModel>), typeof(MetricInputsControl), new PropertyMetadata(null));

    public MetricInputsControl() => InitializeComponent();

    public IReadOnlyList<MetricInputViewModel>? Inputs
    {
        get => (IReadOnlyList<MetricInputViewModel>?)GetValue(InputsProperty);
        set => SetValue(InputsProperty, value);
    }

    // LiveSetting alone announces nothing: the region raises LiveRegionChanged when the error appears.
    private void ErrorText_Loaded(object sender, RoutedEventArgs e)
    {
        var text = (TextBlock)sender;
        text.Loaded -= ErrorText_Loaded;
        text.RegisterPropertyChangedCallback(VisibilityProperty, (d, _) =>
        {
            if (d is TextBlock { Visibility: Visibility.Visible } shown)
            {
                (FrameworkElementAutomationPeer.FromElement(shown) ?? FrameworkElementAutomationPeer.CreatePeerForElement(shown))
                    ?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
            }
        });
    }
}
