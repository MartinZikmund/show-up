using System.ComponentModel;
using ShowUp.Core.Storage;
using ShowUp.Core.ViewModels;

namespace ShowUp.Controls;

/// <summary>Today's content: greeting, a card group per active program and bonus workouts. Self-contained so it can move into the calendar page later.</summary>
public sealed partial class TodayPanel : UserControl
{
    public static readonly DependencyProperty ViewModelProperty = DependencyProperty.Register(
        nameof(ViewModel), typeof(TodayViewModel), typeof(TodayPanel),
        new PropertyMetadata(null, (d, e) => ((TodayPanel)d).OnViewModelChanged()));

    // Each marks square draws its program's marks; recycled cards can move to another program.
    private readonly Dictionary<MarkerCrossControl, TodayProgramViewModel> _crosses = [];
    private readonly HashSet<TodayProgramViewModel> _watched = [];

    public TodayPanel() => InitializeComponent();

    public TodayViewModel? ViewModel
    {
        get => (TodayViewModel?)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    public Visibility NoBonuses(bool hasBonuses) => hasBonuses ? Visibility.Collapsed : Visibility.Visible;

    public static string ProgramLine(string? program, string dayText) =>
        string.IsNullOrEmpty(dayText) ? program ?? "" : $"{program} · {dayText}";

    private void OnViewModelChanged()
    {
        foreach (var program in _watched)
        {
            program.PropertyChanged -= Program_PropertyChanged;
        }

        _watched.Clear();
        Bindings.Update();
        foreach (var cross in _crosses.Keys.ToList())
        {
            Track(cross);
        }
    }

    private void Program_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is TodayProgramViewModel program
            && e.PropertyName is nameof(TodayProgramViewModel.MarkStrokes) or nameof(TodayProgramViewModel.Seed) or nameof(TodayProgramViewModel.Inks))
        {
            foreach (var (cross, owner) in _crosses)
            {
                if (owner == program)
                {
                    ApplyMarks(cross, program, animate: program.AnimateMarks);
                }
            }
        }
    }

    private void Cross_Loaded(object sender, RoutedEventArgs e)
    {
        var cross = (MarkerCrossControl)sender;
        cross.DataContextChanged -= Cross_DataContextChanged;
        cross.DataContextChanged += Cross_DataContextChanged;
        Track(cross);
    }

    private void Cross_Unloaded(object sender, RoutedEventArgs e)
    {
        var cross = (MarkerCrossControl)sender;
        cross.DataContextChanged -= Cross_DataContextChanged;
        _crosses.Remove(cross);
    }

    private void Cross_DataContextChanged(FrameworkElement sender, DataContextChangedEventArgs args) => Track((MarkerCrossControl)sender);

    private void Track(MarkerCrossControl cross)
    {
        if ((cross.DataContext as TodaySessionViewModel)?.Program is not { } program)
        {
            _crosses.Remove(cross);
            return;
        }

        _crosses[cross] = program;
        if (_watched.Add(program))
        {
            program.PropertyChanged += Program_PropertyChanged;
        }

        ApplyMarks(cross, program, animate: false);
    }

    private static void ApplyMarks(MarkerCrossControl cross, TodayProgramViewModel program, bool animate)
    {
        // Seed first: a seed change redraws, which would cut the stroke animation short.
        cross.Seed = program.Seed;
        cross.Inks = program.Inks;
        cross.AnimateChanges = animate;
        cross.Strokes = program.MarkStrokes;
        cross.AnimateChanges = true;
    }

    private void ProGate_Loaded(object sender, RoutedEventArgs e) => ((ProGateHint)sender).Command = ViewModel?.OpenProCommand;

    private void RemoveBonus_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is { } viewModel && ((FrameworkElement)sender).Tag is BonusWorkoutEntity bonus)
        {
            viewModel.RemoveBonusCommand.Execute(bonus);
        }
    }
}
