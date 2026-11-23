# Statistics

The Statistics page shows how much you actually did: workouts per week, what kind they were, a 26-week consistency heatmap and how your programs compare. Open it from **History → Statistics**. It is a page under History, not a tab of its own.

## What it shows

- **Totals**: workouts, active days and time logged.
- **Workouts per week**: one bar per week, ending with the current one. Weeks start on the culture's first day of the week.
- **Workouts by kind**: cardio, strength and so on, most workouts first, with the time under the name where it is known.
- **Consistency**: 26 weeks, one column per week, one square per day. A stronger square means more workouts that day (three or more look the same). Each square's name says the date and the count, so the colour is never the only signal. On a phone it scrolls sideways and starts at the newest week.
- **Programs**: every program you started, with its completion rate, days crossed out of days due, and best streak. A program that ended stops counting days on the day it ended.

A *workout* is a crossed session that isn't rest, or a bonus workout. A session counts on its **calendar day**, like the X on paper, not on the day you drew the cross: catching up five days on Sunday does not look like five workouts on Sunday.

## Why bars count workouts, not minutes

The catalog verifies calendars, not durations: about two thirds of its workouts have no known length (most of Insanity and P90X among them). Minutes would rank those programs at zero, so bars and totals count workouts. Time is shown only where it is known: the catalog length of a session, or what you entered for a bonus workout. That is why the tile says *Time logged*, and why a kind with no known length has no time line instead of "0 min".

## Free and Pro

| | Free | Pro (`ProFeature.FullHistory`) |
|---|---|---|
| Data read | the current program, last 28 days | every program, all bonus workouts |
| Week bars | 4 | 12 |
| Workouts by kind | yes | yes |
| Heatmap, program comparison | behind the Pro gate row | yes |

Free never loads what it hides, the same rule as [History](./history.md).

## Where it lives

- `StatsCalculator` (Core, pure): `Build` turns `ActivityEntry` rows into a `StatsSummary`; `Compare` makes one `ProgramComparison`.
- `StatsService` (`IStatsService`): collects the entries from `IActiveEnrollmentService` and the catalog. Its constants hold the limits (`FreeDays`, `FreeWeeks`, `FullWeeks`, `HeatmapWeeks`).
- `StatsViewModel`: formats the summary and reloads on `TrackingChangedMessage`, `DataChangedMessage` and at midnight (`LiveRefresher`).
- `StatsView`: plain XAML, no chart library. Bars are `ProgressBar`s; the heatmap is two nested `ItemsRepeater`s.
