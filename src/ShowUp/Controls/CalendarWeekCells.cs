using ShowUp.Core.ViewModels;

namespace ShowUp.Controls;

/// <summary>
/// One program week: seven equal, square columns (a short last week leaves the rest empty).
/// Reuses its cells when a recycled row gets a different week.
/// </summary>
public sealed partial class CalendarWeekCells : Panel
{
    public const int Columns = 7;

    /// <summary>Cells never grow past this, so a wide window doesn't get poster-sized days.</summary>
    public const double MaxCellSize = 136;

    public static readonly DependencyProperty DaysProperty = DependencyProperty.Register(
        nameof(Days), typeof(IReadOnlyList<CalendarDayViewModel>), typeof(CalendarWeekCells),
        new PropertyMetadata(null, (d, _) => ((CalendarWeekCells)d).SyncCells()));

    public event TypedEventHandler<CalendarDayCell, CalendarCellAction>? ActionRequested;

    public IReadOnlyList<CalendarDayViewModel>? Days
    {
        get => (IReadOnlyList<CalendarDayViewModel>?)GetValue(DaysProperty);
        set => SetValue(DaysProperty, value);
    }

    private static double Spacing(double width) => width < 500 ? 4 : 8;

    private static double CellSize(double width) =>
        Math.Min(MaxCellSize, Math.Max(0, (width - Spacing(width) * (Columns - 1)) / Columns));

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsInfinity(availableSize.Width) ? Columns * MaxCellSize : availableSize.Width;
        var size = CellSize(width);
        foreach (var child in Children)
        {
            child.Measure(new Size(size, size));
        }

        return new Size(Math.Min(width, size * Columns + Spacing(width) * (Columns - 1)), size);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var size = CellSize(finalSize.Width);
        var step = size + Spacing(finalSize.Width);
        for (var i = 0; i < Children.Count; i++)
        {
            Children[i].Arrange(new Rect(i % Columns * step, 0, size, size));
        }

        return finalSize;
    }

    private void SyncCells()
    {
        var days = Days ?? [];
        while (Children.Count > days.Count)
        {
            var cell = (CalendarDayCell)Children[^1];
            cell.ActionRequested -= Cell_ActionRequested;
            cell.Day = null;
            Children.RemoveAt(Children.Count - 1);
        }

        for (var i = 0; i < days.Count; i++)
        {
            if (i >= Children.Count)
            {
                CalendarDayCell cell = new();
                cell.ActionRequested += Cell_ActionRequested;
                Children.Add(cell);
            }

            ((CalendarDayCell)Children[i]).Day = days[i];
        }
    }

    private void Cell_ActionRequested(CalendarDayCell sender, CalendarCellAction args) => ActionRequested?.Invoke(sender, args);
}
