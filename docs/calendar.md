# The program calendar

The Calendar page is the paper calendar: one row per program week, tap a day to cross off its
next workout. This page covers how it's put together and where to hook in.

## Crossing goes through one service

`IActiveEnrollmentService` (`src/ShowUp.Core/Tracking`) is the only path that writes tracking data
for the active enrollment:

```csharp
using ShowUp.Core.Tracking;

ActiveEnrollment? active = await enrollments.GetAsync(); // program at its pinned revision, track, calendar
await enrollments.CrossAsync(dayIndex, sessionIndex, inkJson); // dayDate comes from active.Calendar
await enrollments.UncrossAsync(dayIndex, sessionIndex);
await enrollments.ShiftAsync(today, days); // repository clamps: crossed days never move
await enrollments.LogBonusAsync("Evening run", date);
```

Callers never pass a date for a cross, so a shifted schedule can't cross the wrong day. Every write
raises `Changed` and sends `TrackingChangedMessage` (`ShowUp.Core.Messages`) on the injected
`IMessenger` (`WeakReferenceMessenger.Default` in the app). With several active enrollments, the
newest one whose program and track are still in the catalog wins. If its pinned revision has left
the catalog, the latest revision of that program is used and a warning is logged.

## The view model

`ProgramCalendarViewModel` builds `Weeks` (`CalendarWeekViewModel`: phase, week number, label,
up to seven `CalendarDayViewModel`s) from `ProgramTrack.Locate`. Gap days from shifts are not
drawn; each cell shows its day number big and its (shifted) date small.

`CalendarDayViewModel` is updated in place on every refresh, so only cells whose state changed
animate. `MarkStrokes` comes from `MarkerAppearance.StrokesFor` and drawn sessions are in `Inks`;
`Seed` is `MarkerGeometry.SeedFrom` of the day's earliest completion by `CompletedAt` (the day index
before that), so uncrossing and re-crossing a session never reshapes the others. `CrossNext`
generates the completion id before the write (`CrossAsync(..., completionId)`), so the cross moment
draws the same X the cell will.
All-optional days count as rest (`DayStatus.CountsAsRest`).

Tapping a day (`CrossNextCommand`):

| Day | What happens |
| --- | --- |
| Future | `ShakeRequested` is set, nothing is written |
| Nothing left (`CanCross` false) | the cell offers "Uncross last session" instead (a day detail sheet replaces this later) |
| Otherwise, Draw it yourself | `ICrossMoment.DrawAsync` with `MarkerAppearance.Outcomes`, then `CrossAsync` with the ink |
| Otherwise, Auto marker | `CrossAsync`, then the cross moment if `CrossMomentPolicy.ShouldShow(mode, isToday)`, else the X animates in the cell |

The lowest undone session is crossed, so a complete day still crosses a remaining optional
session. A second tap on a day while its write is in flight is ignored. When a cross moment shows
the X big, the cell lands it without animating. A shift with nothing left to move says so through
`IDialogService`.

The view model refreshes on `TrackingChangedMessage` (skipped while its own write is in flight,
since it refreshes itself right after), at local midnight (a `TimeProvider` timer,
so it's unit-tested with `FakeTimeProvider`) and when the window is activated on a new day.

## The control

`ProgramCalendarControl` (`src/ShowUp/Controls`) holds the header (program name, streak with the
vector flame, Shift schedule), the grid and the empty state, and takes the view model as
`ViewModel`. `ProgramCalendarView` hosts it; Today embeds it side by side from 1008 px.

- `CalendarWeekCells` is a panel of seven equal square columns, capped at 136 px per cell.
- `CalendarDayCell` is a `Button` filled from code (not x:Bind), so a recycled row lands its
  crosses instantly. It names itself for screen readers ("Day 5, Pure Cardio and Cardio Abs, 1 of
  2 done, today, Nov 21") and carries the `CalendarDay{N}` automation id. Right-click or
  long-press: Uncross, Log bonus workout here. The X and drawn ink span the whole cell like a
  marker on paper; the day number and date sit above them on a translucent backplate
  (`CalendarCellPlateBrush`), and the bonus sticker sits on top. Missed training days
  get a faint hatch. Below 64 px a cell shows only the day number, session ticks and "Rest".

The shift and bonus dialogs live behind `ICalendarPrompts`; the empty state's Browse programs
goes through `ISectionNavigator` (the window's `WindowShellViewModel`).
