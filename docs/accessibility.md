# Accessibility

What the app does for keyboard, screen-reader, reduced-motion and high-contrast users, what was checked, and what was not.

## Keyboard

- **Calendar grid.** The grid is one tab stop. Inside it, the arrow keys move between days, Left and Right wrap to the previous and next week, Up and Down keep the column (clamped in a short last week), Home and End go to the first and last day of the week. Enter or Space crosses the day, or opens its details once it is crossed; the context menu key opens the day's menu.
- **Tab lands on today.** Tabbing into the grid focuses today's cell instead of day 1, also when today's week is scrolled out of view. Focus that comes back after a dialog or the cross moment closes stays on the day you were on.
- **Cross moment.** Escape, Enter or Space dismiss it; while drawing, Escape cancels. *Do it for me* is the keyboard and screen-reader route past the drawing square. The moment of a completed program waits on *Finish program* / *Keep it open*; there, Escape or a tap outside means *Keep it open*.
- Where the focus goes is decided by `CalendarFocusMap.Move` (Core, tested); `ProgramCalendarControl` applies it.

## Screen readers

- Day cells read as one sentence: "Day 5, Boxing: Power Uppercuts, not done, today, Nov 23".
- The cross moment announces its caption once, as a live region; a milestone ("Week 1 done") is the first sentence.
- Statistics: every bar, every heatmap square and every program row has a name with the date and the count ("Week of November 15. Workouts: 6."), carried by a text element, because a `Grid` or `StackPanel` has no automation peer and a name set on one is never read. Time is named only where it is known. Colour is never the only signal.
- Results: each cell reads its metric, column, value and change.
- The drawing square has a name and a hint that points to *Do it for me*. Dialogs are named by their title.
- Decorative elements (paper texture, hero decoration, confetti, the marker X itself) are out of the automation tree.

## Reduced motion, sound, haptics

- With the system's animations off (`IMotionPreference`), the marker X appears drawn, the cross moment does not fade, and there is no confetti. There is no confetti in high contrast either.
- Marker sound and the haptic tick each have a switch in Settings.

## Contrast and high contrast

Measured for the muted text colour, the lowest-contrast text in the app (WCAG AA needs 4.5:1 for normal text):

| Theme | On the page | On a calendar cell |
|---|---|---|
| Light (`#6E6656`) | 5.04:1 | 5.58:1 |
| Dark (`#A9A49A`) | 6.14:1 | 5.37:1 |

In high contrast the app's own colours map to system colours (`Colors.xaml`, `HighContrast` dictionary), the paper grain is off, and the hero decoration is hidden.

## Touch targets

Icon-only buttons are at least 40 × 40 (the bonus workout's delete button was 30 × 22 before). Text buttons keep the Fluent 32 px height.

## Checked, and not checked

Checked on Windows (2026-11-23), with the keyboard only: every arrow move, Home, End and row wrap in the Calendar; Tab into the grid lands on today; Tab leaves it in one press; Enter on a crossed day opens its sheet and Escape returns focus to that day. With UI Automation: the Statistics page exposes 12 week bars, 182 heatmap squares and the program rows by name.

Known gap: **Shift+Tab into the grid lands on the last day**, not on today. The redirect in `WeeksRepeater_GettingFocus` is not honoured when focus comes back from the title bar.

Not checked yet: Narrator or any other screen reader end to end (the names are in the automation tree; nobody has listened to them), high contrast on every page, arrow keys on the Desktop, WebAssembly and mobile heads (the code is shared, the focus events are per platform), and right-to-left layouts (Left and Right are not mirrored).
