using System.ComponentModel;
using ShowUp.Core.Catalog;
using ShowUp.Core.ViewModels;

namespace ShowUp.Views;

public sealed partial class SessionPickerDialog : ContentDialog
{
    private bool _syncing;

    public SessionPickerDialog(SessionPickerViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();

        KindBox.ItemsSource = SessionPickerViewModel.CustomKinds;
        viewModel.PropertyChanged += ViewModel_PropertyChanged;
        Unloaded += (_, _) => viewModel.PropertyChanged -= ViewModel_PropertyChanged;
        Apply();
    }

    public SessionPickerViewModel ViewModel { get; }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e) => Apply();

    // One-way mirror of the view model into the controls; _syncing keeps the echoes from writing back.
    private void Apply()
    {
        _syncing = true;
        try
        {
            IsPrimaryButtonEnabled = ViewModel.CanSave;
            WorkoutBox.ItemsSource = ViewModel.Suggestions;
            CustomFields.Visibility = ViewModel.IsCustom ? Visibility.Visible : Visibility.Collapsed;
            KindBox.SelectedItem = ViewModel.Kind;
            DurationBox.Value = ViewModel.DurationMinutes ?? double.NaN;
            OptionalBox.IsChecked = ViewModel.Optional;
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

    private void KindBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_syncing && KindBox.SelectedItem is WorkoutKind kind)
        {
            ViewModel.Kind = kind;
        }
    }

    private void DurationBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (!_syncing)
        {
            ViewModel.DurationMinutes = double.IsNaN(args.NewValue) ? null : (int)Math.Round(args.NewValue);
        }
    }

    private void OptionalBox_Changed(object sender, RoutedEventArgs e)
    {
        if (!_syncing)
        {
            ViewModel.Optional = OptionalBox.IsChecked == true;
        }
    }
}
