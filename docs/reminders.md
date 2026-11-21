# Reminders

Workout reminders: a time and weekdays, delivered as a notification that names the day's workout. Delivery per platform is not wired yet; this page covers planning and the UI.

## How it works

- `ReminderPlanner.Plan(...)` turns enabled reminders into `PlannedReminder`s for the next 14 calendar days (today included while its time is ahead). Wall-clock time is kept across DST: a time skipped by spring-forward moves to the next valid minute, a repeated fall-back time uses its first occurrence.
- Days with nothing left to do (everything crossed, or nothing due in any active program) get no notification. A rest day says "Rest day. You earned it."; a workout day says "<workout> is waiting. Show up." With several open sessions the text reads "<first> + N more".
- `ReminderDayProvider` reads each date from `IScheduleQuery`, so it covers all active programs.
- `ReminderCoordinator` re-plans on app start, window activation, `TrackingChangedMessage`, `RemindersChangedMessage` and entitlement changes, then calls `IReminderScheduler.ReplaceAllAsync`. It skips scheduling when `IsSupported` is false.
- Heads currently register `NullReminderScheduler` (`IsSupported` false). The Reminders page says so, but still lets people set reminders up.

## Free and Pro

Free keeps 1 reminder, Pro up to 20 (`FeatureGate.MaxReminders`). On a lapse with several reminders on, the earliest-created enabled one stays on and the rest are disabled but kept. Regaining Pro does not switch them back on; the user decides. The check waits for the first store refresh so a renewal that happened while the app was closed is not treated as a lapse.

## Storing

`ReminderEntity.DaysMask` bit 0 is Monday. The editor shows weekdays in the culture's first-day-of-week order.
