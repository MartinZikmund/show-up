namespace ShowUp.Core.ViewModels;

/// <summary>A color choice in a swatch picker; the picker puts these on each item's container for UI Automation.</summary>
public interface ISwatchItem
{
    string AutomationId { get; }

    string Name { get; }
}
