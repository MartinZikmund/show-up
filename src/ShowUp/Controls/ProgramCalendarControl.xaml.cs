using ShowUp.Core.ViewModels;

namespace ShowUp.Controls;

/// <summary>The program calendar with its header and empty state; the Calendar page hosts it, Today embeds it when wide.</summary>
public sealed partial class ProgramCalendarControl : UserControl
{
    public static readonly DependencyProperty ViewModelProperty = DependencyProperty.Register(
        nameof(ViewModel), typeof(ProgramCalendarViewModel), typeof(ProgramCalendarControl),
        new PropertyMetadata(null));

    public ProgramCalendarControl() => InitializeComponent();

    public ProgramCalendarViewModel? ViewModel
    {
        get => (ProgramCalendarViewModel?)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    private static CalendarWeekCells? CellsOf(UIElement element) =>
        (element as Panel)?.Children.OfType<CalendarWeekCells>().FirstOrDefault();

    private void WeeksRepeater_ElementPrepared(ItemsRepeater sender, ItemsRepeaterElementPreparedEventArgs args)
    {
        if (CellsOf(args.Element) is { } cells)
        {
            cells.ActionRequested -= Cells_ActionRequested;
            cells.ActionRequested += Cells_ActionRequested;
        }
    }

    private void WeeksRepeater_ElementClearing(ItemsRepeater sender, ItemsRepeaterElementClearingEventArgs args)
    {
        if (CellsOf(args.Element) is { } cells)
        {
            cells.ActionRequested -= Cells_ActionRequested;
        }
    }

    private void Cells_ActionRequested(CalendarDayCell cell, CalendarCellAction action)
    {
        if (ViewModel is not { } viewModel || cell.Day is not { } day)
        {
            return;
        }

        switch (action)
        {
            case CalendarCellAction.Cross:
                viewModel.CrossNextCommand.Execute(day);
                break;
            case CalendarCellAction.Uncross:
                viewModel.UncrossLastCommand.Execute(day);
                break;
            case CalendarCellAction.LogBonus:
                viewModel.LogBonusCommand.Execute(day);
                break;
        }
    }
}
