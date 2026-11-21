using ShowUp.Core.Entitlements;
using ShowUp.Core.ViewModels;

namespace ShowUp.Views;

public partial class ProViewBase : ViewBase<ProViewModel> { }

/// <summary>Reached from Settings and from every gate prompt, so it keeps whichever section was open.</summary>
public sealed partial class ProView : ProViewBase
{
    public ProView() => InitializeComponent();

    private void OnOfferClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: ProOffer offer })
        {
            ViewModel?.PurchaseCommand.Execute(offer);
        }
    }
}
