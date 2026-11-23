using ShowUp.Core.Navigation;
using ShowUp.Core.ViewModels;

namespace ShowUp.Views;

public partial class StatsViewBase : ViewBase<StatsViewModel> { }

[NavigationInfo(NavigationSection.History)]
public sealed partial class StatsView : StatsViewBase
{
    public StatsView() => InitializeComponent();

    /// <summary>Marker ink over a neutral cell: nothing, then three steps up to full strength.</summary>
    public static double HeatOpacity(int level) => level switch
    {
        <= 0 => 0,
        1 => 0.5,
        2 => 0.75,
        _ => 1,
    };

    public static double CellOpacity(bool isFuture) => isFuture ? 0.35 : 1;

    // The newest weeks are on the right; start there when the heatmap is wider than the page.
    private void HeatWeeks_SizeChanged(object sender, SizeChangedEventArgs e) =>
        HeatScroll.ChangeView(HeatScroll.ScrollableWidth, null, null, disableAnimation: true);
}
