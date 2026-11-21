using ShowUp.Services.Localization;

namespace ShowUp.Services.Dialogs;

public sealed class CalendarPrompts(IDialogCoordinator dialogCoordinator, IXamlRootProvider xamlRootProvider) : ICalendarPrompts
{
    private const int MaxShiftDays = 30;

    public async Task<int?> AskShiftDaysAsync()
    {
        NumberBox days = new()
        {
            Header = Localizer.Instance["ShiftDialogDays"],
            Minimum = 1,
            Maximum = MaxShiftDays,
            Value = 1,
            SmallChange = 1,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline,
            ValidationMode = NumberBoxValidationMode.InvalidInputOverwritten,
        };
        AutomationProperties.SetAutomationId(days, "ShiftDaysNumberBox");

        ContentDialog dialog = new()
        {
            Title = Localizer.Instance["CalendarShiftSchedule"],
            Content = new StackPanel
            {
                Spacing = 16,
                Children =
                {
                    new TextBlock { Text = Localizer.Instance["ShiftDialogMessage"], TextWrapping = TextWrapping.Wrap },
                    days,
                },
            },
            PrimaryButtonText = Localizer.Instance["ShiftDialogConfirm"],
            CloseButtonText = Localizer.Instance["Cancel"],
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = xamlRootProvider.XamlRoot,
        };

        // Cleared or out of range: no Shift until it's a valid count.
        days.ValueChanged += (_, _) => dialog.IsPrimaryButtonEnabled = IsValidShift(days.Value);

        var result = await dialogCoordinator.ShowAsync(dialog);
        return result == ContentDialogResult.Primary && IsValidShift(days.Value) ? (int)Math.Round(days.Value) : null;
    }

    private static bool IsValidShift(double value) => !double.IsNaN(value) && value >= 1 && value <= MaxShiftDays;
}
