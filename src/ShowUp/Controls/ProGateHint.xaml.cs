using System.Windows.Input;

namespace ShowUp.Controls;

/// <summary>"Unlock with Pro" link to sit under whatever a Free user can't use; bind <see cref="Command"/> to the page's open-paywall command.</summary>
public sealed partial class ProGateHint : UserControl
{
    public static readonly DependencyProperty CommandProperty = DependencyProperty.Register(
        nameof(Command), typeof(ICommand), typeof(ProGateHint),
        new PropertyMetadata(null, (d, e) => ((ProGateHint)d).UnlockButton.Command = (ICommand?)e.NewValue));

    public ProGateHint() => InitializeComponent();

    public ICommand? Command
    {
        get => (ICommand?)GetValue(CommandProperty);
        set => SetValue(CommandProperty, value);
    }
}
