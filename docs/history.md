# History

The History tab lists every program you have started and the bonus workouts you logged, so you can see what you actually showed up for.

## What it shows

- **Programs**: active ones first (newest first), then ended ones by when they ended. Each card has the program's accent strip, a status chip (Active, Finished, Stopped), `Day 23 of 63 · 21 crossed`, the longest streak (`StreakCalculator.Longest`), and the dates. Active programs link to their Calendar and, when any workout has metrics, to their Results. Ended programs are read-only: no links yet.
- **Bonus workouts**: grouped by month (culture month names), newest first.
- **Layout**: one column below 1008 px, programs and bonus workouts side by side above it.

## Free and Pro

Free sees the current program, which is the primary one (the most recent ended program when none is active), and the last 30 days of bonus workouts. When more exists, a Pro gate row (`ProGateHint`) opens the paywall. Pro sees everything (`ProFeature.FullHistory`).

## Where it lives

- `HistoryViewModel` (Core) builds the cards and months and reloads on `TrackingChangedMessage` and at midnight (`LiveRefresher`).
- `IActiveEnrollmentService.GetHistoryAsync()` returns every enrollment with its pinned program revision (ended ones included, via `ITrackingRepository.GetAllEnrollmentsAsync()`); `GetHistoryCompletionsAsync(id)` loads one enrollment's crossed sessions, so Free never loads the ones it hides.
- Dates follow the culture: short date for programs, `MonthDayPattern` and `YearMonthPattern` for bonus workouts and month headers.
- The **Statistics** button opens [statistics.md](./statistics.md).
