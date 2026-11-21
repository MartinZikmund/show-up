using ShowUp.Core.Services;
using ShowUp.Core.Tracking;
using ShowUp.Services.Localization;

namespace ShowUp.Services.Dialogs;

public sealed class PrimaryProgramPrompt(IDialogCoordinator dialogCoordinator, IXamlRootProvider xamlRootProvider) : IPrimaryProgramPrompt
{
    public async Task<Guid?> PickAsync(IReadOnlyList<ActiveEnrollment> programs, Guid suggested)
    {
        RadioButtons choices = new();
        AutomationProperties.SetAutomationId(choices, "PrimaryProgramChoices");
        AutomationProperties.SetName(choices, Localizer.Instance["PrimaryProgramTitle"]);
        foreach (var program in programs)
        {
            RadioButton choice = new() { Content = program.Program.Name, Tag = program.Id };
            AutomationProperties.SetAutomationId(choice, $"PrimaryProgram_{program.Id:N}");
            choices.Items.Add(choice);
            if (program.Id == suggested)
            {
                choices.SelectedItem = choice;
            }
        }

        ContentDialog dialog = new()
        {
            Title = Localizer.Instance["PrimaryProgramTitle"],
            Content = new StackPanel
            {
                Spacing = 16,
                Children =
                {
                    new TextBlock { Text = Localizer.Instance["PrimaryProgramMessage"], TextWrapping = TextWrapping.Wrap },
                    choices,
                },
            },
            PrimaryButtonText = Localizer.Instance["PrimaryProgramConfirm"],
            CloseButtonText = Localizer.Instance["Close"],
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = xamlRootProvider.XamlRoot,
        };
        AutomationProperties.SetAutomationId(dialog, "PrimaryProgramDialog");

        var result = await dialogCoordinator.ShowAsync(dialog);
        return result == ContentDialogResult.Primary && (choices.SelectedItem as RadioButton)?.Tag is Guid id ? id : null;
    }
}
