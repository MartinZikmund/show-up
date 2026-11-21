using System.ComponentModel;
using ShowUp.Core.Catalog;
using ShowUp.Core.ViewModels;

namespace ShowUp.Views;

public sealed partial class BonusWorkoutDialog : ContentDialog
{
    private bool _syncing;

    public BonusWorkoutDialog(BonusWorkoutDialogViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();

        DatePicker.MaxDate = ToOffset(viewModel.Today);
        DatePicker.Date = ToOffset(viewModel.Date);
        viewModel.PropertyChanged += ViewModel_PropertyChanged;
        Unloaded += (_, _) => viewModel.PropertyChanged -= ViewModel_PropertyChanged;
        Apply();
    }

    public BonusWorkoutDialogViewModel ViewModel { get; }

    private static DateTimeOffset ToOffset(DateOnly date) => new(date.ToDateTime(TimeOnly.MinValue));

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e) => Apply();

    // One-way mirror of the view model into the controls; _syncing keeps the echoes from writing back.
    private void Apply()
    {
        _syncing = true;
        try
        {
            IsPrimaryButtonEnabled = ViewModel.CanSave;
            WorkoutBox.ItemsSource = ViewModel.Suggestions;
            DurationBox.Value = ViewModel.DurationMinutes ?? double.NaN;
            DatePicker.Date = ToOffset(ViewModel.Date);
            if (WorkoutBox.Text != ViewModel.Query)
            {
                WorkoutBox.Text = ViewModel.Query;
            }
        }
        finally
        {
            _syncing = false;
        }
    }

    private void WorkoutBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (!_syncing && args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
        {
            ViewModel.Query = sender.Text;
        }
    }

    private void WorkoutBox_SuggestionChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args)
    {
        if (args.SelectedItem is Workout workout)
        {
            ViewModel.Selected = workout;
        }
    }

    private void DurationBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (!_syncing)
        {
            ViewModel.DurationMinutes = double.IsNaN(args.NewValue) ? null : (int)Math.Round(args.NewValue);
        }
    }

    private void DatePicker_DateChanged(CalendarDatePicker sender, CalendarDatePickerDateChangedEventArgs args)
    {
        if (_syncing)
        {
            return;
        }

        ViewModel.SetDate(args.NewDate is { } date ? DateOnly.FromDateTime(date.DateTime) : null);
        if (args.NewDate is null)
        {
            Apply();
        }
    }
}
