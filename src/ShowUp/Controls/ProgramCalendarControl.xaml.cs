using Microsoft.UI.Xaml.Input;
using ShowUp.Core.Tracking;
using ShowUp.Core.ViewModels;
using VirtualKey = Windows.System.VirtualKey;

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
            case CalendarCellAction.Details:
                viewModel.OpenDayCommand.Execute(day);
                break;
        }
    }

    // Arrow keys, Home and End walk the days; the whole grid is one tab stop.
    private void WeeksRepeater_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        CalendarFocusMove? move = e.Key switch
        {
            VirtualKey.Left => CalendarFocusMove.Left,
            VirtualKey.Right => CalendarFocusMove.Right,
            VirtualKey.Up => CalendarFocusMove.Up,
            VirtualKey.Down => CalendarFocusMove.Down,
            VirtualKey.Home => CalendarFocusMove.Home,
            VirtualKey.End => CalendarFocusMove.End,
            _ => null,
        };
        if (move is null || ViewModel is not { } viewModel || CellOf(e.OriginalSource) is not { } cell || Locate(cell) is not { } from)
        {
            return;
        }

        int[] lengths = [.. viewModel.Weeks.Select(w => w.Days.Count)];
        if (CalendarFocusMap.Move(from.Week, from.Day, move.Value, lengths) is { } target)
        {
            e.Handled = true;
            FocusCell(target.Week, target.Day);
        }
    }

    // Tab lands on today, not on day 1 of a 90-day program.
    private void WeeksRepeater_GettingFocus(UIElement sender, GettingFocusEventArgs args)
    {
        // Only keyboard focus arriving from outside the grid: arrow moves and restored focus are left alone.
        if (args.InputDevice != FocusInputDeviceKind.Keyboard
            || CellOf(args.OldFocusedElement) is not null
            || ViewModel is not { } viewModel)
        {
            return;
        }

        for (int week = 0; week < viewModel.Weeks.Count; week++)
        {
            int day = viewModel.Weeks[week].Days.ToList().FindIndex(d => d.IsToday);
            if (day >= 0)
            {
                if (WeeksRepeater.TryGetElement(week) is { } element && CellsOf(element) is { } cells
                    && day < cells.Children.Count && !ReferenceEquals(cells.Children[day], args.NewFocusedElement))
                {
                    args.TrySetNewFocusedElement(cells.Children[day]);
                }

                return;
            }
        }
    }

    private static CalendarDayCell? CellOf(object? source)
    {
        for (var node = source as DependencyObject; node is not null; node = VisualTreeHelper.GetParent(node))
        {
            if (node is CalendarDayCell cell)
            {
                return cell;
            }
        }

        return null;
    }

    private (int Week, int Day)? Locate(CalendarDayCell cell)
    {
        if (VisualTreeHelper.GetParent(cell) is not CalendarWeekCells cells || VisualTreeHelper.GetParent(cells) is not UIElement weekElement)
        {
            return null;
        }

        int week = WeeksRepeater.GetElementIndex(weekElement);
        int day = cells.Children.IndexOf(cell);
        return week < 0 || day < 0 ? null : (week, day);
    }

    private void FocusCell(int week, int day)
    {
        // A week scrolled far away isn't realized yet: ask for it.
        var element = WeeksRepeater.TryGetElement(week) ?? WeeksRepeater.GetOrCreateElement(week);
        if (CellsOf(element) is { } cells && day < cells.Children.Count && cells.Children[day] is CalendarDayCell cell)
        {
            cell.Focus(FocusState.Keyboard);
            cell.StartBringIntoView();
        }
    }

    private void ProgramSwitcher_DropDownOpened(object sender, object e) =>
        ShowUp.Controls.ProgramSwitcherAutomation.TagContainers((ComboBox)sender);
}
