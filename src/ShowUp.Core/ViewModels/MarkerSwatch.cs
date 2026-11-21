using ShowUp.Core.Marking;

namespace ShowUp.Core.ViewModels;

/// <summary>One choice in the Settings marker color picker.</summary>
public sealed partial class MarkerSwatch(MarkerColor color, string name) : ObservableObject
{
    public MarkerColor Color { get; } = color;

    /// <summary>Localized color name; also the automation name and, with <see cref="Color"/>, the AutomationId suffix.</summary>
    public string Name { get; } = name;

    public string AutomationId => $"MarkerColor_{Color}";

    [ObservableProperty]
    public partial bool IsSelected { get; set; }
}
