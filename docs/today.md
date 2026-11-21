# Today and bonus workouts

What is on today, how to cross it off, and how to log a workout that is not in your program.

## The page

`TodayView` hosts `TodayPanel` (`src/ShowUp/Controls/TodayPanel.xaml`), a self-contained control bound to
`TodayViewModel`. At 1008 px and wider the view puts the panel (400 px) beside `ProgramCalendarControl`;
narrower, it is the panel alone. Because the panel owns all of Today's content, it can move into the
calendar page later without touching the view model.

The panel shows a greeting by time of day, the program and day ("Day 12 of 60"), the streak, and one card
per session today: name, kind chip, duration, an "Optional" badge, and a **Cross it off** button. A crossed
session shows the day's marks on a paper square. Other states:

- **Rest day** (`DayStatus.CountsAsRest`): a calm card; rest sessions can still be crossed.
- **Nothing scheduled** (before the start, after the end, or a gap from a shift): a short note.
- **No program**: an invitation to Programs. Bonus workouts still work.

## One crossing flow

`ICrossingCoordinator` (`CrossingCoordinator` in Core) runs the draw-or-auto choice, writes through
`IActiveEnrollmentService.CrossAsync` with a completion id made up front, and plays the cross moment. The
calendar and Today both call it with a `CrossTarget` and `CrossHooks` (their own write guard and refresh).

## Bonus workouts

`IBonusWorkoutDialog` opens `BonusWorkoutDialog` (a `ContentDialog`) over `BonusWorkoutDialogViewModel`:

- Search every catalog workout by name (case and diacritics ignored, family shown, at most 20), or type a
  custom title.
- Duration is prefilled from the workout; the day defaults to today, past days are allowed, future days
  are not.
- Log stays disabled until the title and day are valid.

Today lists the last seven days of bonuses with a remove button (confirmation first). The calendar cell's
"Log bonus workout here" opens the same dialog. Writes go through `IActiveEnrollmentService`
(`LogBonusAsync`, `RemoveBonusAsync`, `GetBonusWorkoutsAsync(from, to)`), which need no enrollment.
`SqliteTrackingRepository.LogBonusAsync` throws `ArgumentException` for blank titles and future dates.

## Streak

The streak on Today comes from `StreakCalculator.Current` over the active enrollment's days (same as the
calendar header). Bonus workouts never count toward it.
