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
}
