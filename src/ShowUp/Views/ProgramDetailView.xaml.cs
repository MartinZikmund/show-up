using System.ComponentModel;
using ShowUp.Core.Navigation;
using ShowUp.Core.ViewModels;

namespace ShowUp.Views;

public partial class ProgramDetailViewBase : ViewBase<ProgramDetailViewModel> { }

[NavigationInfo(NavigationSection.Programs)]
public sealed partial class ProgramDetailView : ProgramDetailViewBase
{
    private bool _syncingTrack;

    public ProgramDetailView()
    {
        InitializeComponent();
        Loaded += (_, _) => Attach();
        Unloaded += (_, _) => Detach();
    }

    public static double DayOpacity(bool isRest) => isRest ? 0.55 : 1;

    private void Attach()
    {
        if (ViewModel is not { } viewModel)
        {
            return;
        }

        viewModel.PropertyChanged -= ViewModel_PropertyChanged;
        viewModel.PropertyChanged += ViewModel_PropertyChanged;
        FillTrackBar();
    }

    private void Detach()
    {
        if (ViewModel is { } viewModel)
        {
            viewModel.PropertyChanged -= ViewModel_PropertyChanged;
        }
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ProgramDetailViewModel.Tracks))
        {
            FillTrackBar();
        }
    }

    // SelectorBar has no ItemsSource, so the items follow the track list by hand.
    private void FillTrackBar()
    {
        if (ViewModel is not { } viewModel)
        {
            return;
        }

        _syncingTrack = true;
        TrackBar.Items.Clear();
        foreach (var track in viewModel.Tracks)
        {
            TrackBar.Items.Add(new SelectorBarItem { Text = track.Name });
        }

        if (viewModel.SelectedTrack is { } selected && viewModel.Tracks.ToList().IndexOf(selected) is var index and >= 0)
        {
            TrackBar.SelectedItem = TrackBar.Items[index];
        }

        _syncingTrack = false;
    }

    private void TrackBar_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        if (_syncingTrack || ViewModel is not { } viewModel)
        {
            return;
        }

        int index = sender.Items.IndexOf(sender.SelectedItem);
        if (index >= 0 && index < viewModel.Tracks.Count)
        {
            viewModel.SelectedTrack = viewModel.Tracks[index];
        }
    }
}
