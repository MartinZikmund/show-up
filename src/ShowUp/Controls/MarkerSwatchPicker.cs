using ShowUp.Core.ViewModels;

namespace ShowUp.Controls;

/// <summary>Swatch list that puts each swatch's automation id and name on its container, where UIA and tests look.</summary>
public sealed partial class MarkerSwatchPicker : ListView
{
    protected override void PrepareContainerForItemOverride(DependencyObject element, object item)
    {
        base.PrepareContainerForItemOverride(element, item);
        if (item is ISwatchItem swatch)
        {
            AutomationProperties.SetAutomationId(element, swatch.AutomationId);
            AutomationProperties.SetName(element, swatch.Name);
        }
    }
}
