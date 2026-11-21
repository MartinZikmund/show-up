using System.ComponentModel;
using ShowUp.Core.Storage;
using ShowUp.Core.ViewModels;

namespace ShowUp.Controls;

/// <summary>Today's content: greeting, session cards and bonus workouts. Self-contained so it can move into the calendar page later.</summary>
public sealed partial class TodayPanel : UserControl
{
    public static readonly DependencyProperty ViewModelProperty = DependencyProperty.Register(
        nameof(ViewModel), typeof(TodayViewModel), typeof(TodayPanel),
        new PropertyMetadata(null, (d, e) => ((TodayPanel)d).OnViewModelChanged(e.OldValue as TodayViewModel, e.NewValue as TodayViewModel)));

    private readonly List<MarkerCrossControl> _crosses = [];

    public TodayPanel() => InitializeComponent();

    public TodayViewModel? ViewModel
    {
        get => (TodayViewModel?)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    public Visibility NoBonuses(bool hasBonuses) => hasBonuses ? Visibility.Collapsed : Visibility.Visible;

    public string ProgramLine(string? program, string dayText) =>
        string.IsNullOrEmpty(dayText) ? program ?? "" : $"{program} · {dayText}";

    private void OnViewModelChanged(TodayViewModel? oldValue, TodayViewModel? newValue)
    {
        if (oldValue is not null)
        {
            oldValue.PropertyChanged -= ViewModel_PropertyChanged;
        }

        if (newValue is not null)
        {
            newValue.PropertyChanged += ViewModel_PropertyChanged;
        }

        Bindings.Update();
        foreach (var cross in _crosses)
        {
            ApplyMarks(cross, animate: false);
        }
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(TodayViewModel.MarkStrokes) or nameof(TodayViewModel.Seed) or nameof(TodayViewModel.Inks))
        {
            foreach (var cross in _crosses)
            {
                ApplyMarks(cross, animate: ViewModel?.AnimateMarks ?? false);
            }
        }
    }

    private void Cross_Loaded(object sender, RoutedEventArgs e)
    {
        var cross = (MarkerCrossControl)sender;
        if (!_crosses.Contains(cross))
        {
            _crosses.Add(cross);
        }

        ApplyMarks(cross, animate: false);
    }

    private void Cross_Unloaded(object sender, RoutedEventArgs e) => _crosses.Remove((MarkerCrossControl)sender);

    private void ApplyMarks(MarkerCrossControl cross, bool animate)
    {
        if (ViewModel is not { } viewModel)
        {
            return;
        }

        // Seed first: a seed change redraws, which would cut the stroke animation short.
        cross.Seed = viewModel.Seed;
        cross.Inks = viewModel.Inks;
        cross.AnimateChanges = animate;
        cross.Strokes = viewModel.MarkStrokes;
        cross.AnimateChanges = true;
    }

    private void RemoveBonus_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is { } viewModel && ((FrameworkElement)sender).Tag is BonusWorkoutEntity bonus)
        {
            viewModel.RemoveBonusCommand.Execute(bonus);
        }
    }
}
