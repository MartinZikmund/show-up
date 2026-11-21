using System.Windows.Input;

namespace ShowUp.Controls;

/// <summary>"Unlock with Pro" link to sit under whatever a Free user can't use; bind <see cref="Command"/> to the page's open-paywall command.</summary>
public sealed partial class ProGateHint : UserControl
{
    public static readonly DependencyProperty CommandProperty = DependencyProperty.Register(
        nameof(Command), typeof(ICommand), typeof(ProGateHint),
        new PropertyMetadata(null, (d, e) => ((ProGateHint)d).UnlockButton.Command = (ICommand?)e.NewValue));

    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(ProGateHint),
        new PropertyMetadata(null, (d, e) => ((ProGateHint)d).ApplyText()));

    public ProGateHint()
    {
        InitializeComponent();
        _defaultText = LinkText.Text;
    }

    private readonly string _defaultText;

    /// <summary>Overrides "Unlock with Pro" with a line that says what Pro unlocks here.</summary>
    public string? Text
    {
        get => (string?)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    private void ApplyText()
    {
        LinkText.Text = string.IsNullOrEmpty(Text) ? _defaultText : Text;
        AutomationProperties.SetName(UnlockButton, LinkText.Text);
    }

    public ICommand? Command
    {
        get => (ICommand?)GetValue(CommandProperty);
        set => SetValue(CommandProperty, value);
    }
}
