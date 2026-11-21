using Microsoft.UI.Xaml.Media;
using ShowUp.Controls;
using ShowUp.Core.Navigation;
using ShowUp.Core.ViewModels;

namespace ShowUp.Views;

public partial class ScheduleBuilderViewBase : ViewBase<ScheduleBuilderViewModel> { }

[NavigationInfo(NavigationSection.Programs)]
public sealed partial class ScheduleBuilderView : ScheduleBuilderViewBase
{
    public ScheduleBuilderView() => InitializeComponent();

    public static Visibility Shows(string? text) => string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;

    public static Brush BrushOf(string hex) => new SolidColorBrush(ProgramHero.ParseColor(hex));

    private void WideWeeks_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ViewModel is { } viewModel && WideWeeks.SelectedItem is BuilderWeekViewModel week)
        {
            viewModel.SelectedWeek = week;
        }
    }
}
