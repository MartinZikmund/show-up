# Reminders

Workout reminders: a time and weekdays, delivered as a notification that names the day's workout. This page covers planning, the UI, and how each head delivers them.

## How it works

- `ReminderPlanner.Plan(...)` turns enabled reminders into `PlannedReminder`s for the next 14 calendar days (today included while its time is ahead). Wall-clock time is kept across DST: a time skipped by spring-forward moves to the next valid minute, a repeated fall-back time uses its first occurrence.
- Days with nothing left to do (everything crossed, or nothing due in any active program) get no notification. A rest day says "Rest day. You earned it."; a workout day says "<workout> is waiting. Show up." With several open sessions the text reads "<first> + N more".
- `ReminderDayProvider` reads each date from `IScheduleQuery`, so it covers all active programs.
- `ReminderCoordinator` re-plans on app start, main window activation, `TrackingChangedMessage`, `RemindersChangedMessage` and entitlement changes, then calls `IReminderScheduler.ReplaceAllAsync`. It skips scheduling when `IsSupported` is false.
- `IReminderScheduler.IsDeniedAsync()` reports notifications switched off without prompting; the Reminders page re-checks it on load while a reminder is on.
- `IReminderScheduler.Limit` says where delivery stops short of the OS (`WhileAppRunning`, `WhileTabOpen`); the Reminders page shows a note for it.
- Tapping a notification sends `ReminderOpenedMessage`; `WindowShell` switches to Today and brings the window forward. A cold start lands on Today anyway.

## Delivery per head

`App.RegisterReminderScheduler` picks the implementation. Every OS id starts with `ReminderNotifications.KeyPrefix` and is derived from (reminder id, fire time), so a re-plan removes ours and only ours.

| Head | Scheduler | Delivery | Permission | Limits |
|---|---|---|---|---|
| Android | `AndroidReminderScheduler` (`Platforms/Android/Reminders`) | `AlarmManager` alarm → `ReminderAlarmReceiver` posts a notification on channel `reminders`. Works with the app closed. | `POST_NOTIFICATIONS` asked at runtime on Android 13+; the Reminders page re-checks `AreNotificationsEnabled` on load. | Exact (`SetExactAndAllowWhileIdle`) only while `SCHEDULE_EXACT_ALARM` is allowed (`CanScheduleExactAlarms()`); otherwise `SetAndAllowWhileIdle`, which Doze can delay by several minutes. |
| iOS | `IosReminderScheduler` (`Platforms/iOS/Reminders`) | `UNCalendarNotificationTrigger` per reminder, non-repeating, floating in the device's zone. Shown in the foreground too. | Alert + sound, asked on the first reminder. | iOS keeps 64 pending per app; we send the 60 soonest (14 days × several reminders can exceed that). Re-planning on every launch tops it up. |
| Windows (packaged) | `WindowsReminderScheduler` (`Platforms/Windows/Reminders`) | `ToastNotifier.AddToSchedule` scheduled toasts (group `reminders`, tag = key), content from `AppNotificationBuilder`. Clicks activate the app through the manifest COM server and `AppNotificationManager.Register()`. | None; reports "denied" when notifications are off in Settings. | Windows drops a scheduled toast if the PC was off more than 5 minutes past its time. Unpackaged runs report `IsSupported = false`. |
| WebAssembly | `BrowserReminderScheduler` + `WasmScripts/Reminders.js` (a missing script reports unsupported) | `setTimeout` per reminder in the next 24 h, shown with the `Notification` API. Android Chrome refuses `new Notification`; the fallback uses `showNotification` only if a service worker is registered (the app doesn't register one today), and a click on that one doesn't route to Today. An hourly refresh and every re-plan on focus move the window along. | `Notification.requestPermission()` on the first reminder. | **Only while a tab with the app is open** - no push server; the note points to the native apps. The prompt is started before the reminder is saved so it stays inside the click's user gesture. Reminders found more than 10 minutes late (sleep) are dropped. |
| Desktop (Skia: Windows, macOS, Linux) | `InAppReminderScheduler` (Core) | Checks the clock every 15 s and shows an `InfoBar` banner (neutral bell icon) at the top of the last activated window plus the marker sound. | None. | **Only while the app is running** (minimized is fine). Reminders more than 10 minutes late are dropped. |

### Android details

- The last plan is stored as JSON (`planned-reminders.json` in the app's files dir) so `ReminderRescheduleReceiver` can re-arm alarms after `BOOT_COMPLETED`, `MY_PACKAGE_REPLACED`, `TIME_SET`, `TIMEZONE_CHANGED` and an exact-alarm permission change, without starting the UI. `ReminderNotifications.Rebase` keeps each reminder at its wall-clock time in the new zone.
- The receivers are declared by hand in `AndroidManifest.xml` and named with `[Register("showup.reminders.…")]`; `AndroidManifestTests` keeps the two in step.
- We don't declare `USE_EXACT_ALARM`: Google Play reserves it for alarm clock and calendar apps.

### Windows details

The toast activator CLSID in `Package.appxmanifest` is a placeholder. `StampToastActivatorClsid` (`src/WorktreeIdentity.targets`) writes a UUIDv5 of `ApplicationId` into both the activator and the COM class of the generated manifest, so Dev, Prod and every worktree build get their own and a click never activates another installed build. Toast XML is built by `ReminderToastXml` (escaped); a toast that fails to schedule is logged and skipped. A cold start from a click is routed through `AppInstance.GetActivatedEventArgs()` after `Register()`.

## Free and Pro

Free keeps 1 reminder, Pro up to 20 (`FeatureGate.MaxReminders`). On a lapse with several reminders on, the earliest-created enabled one stays on and the rest are disabled but kept. Regaining Pro does not switch them back on; the user decides. The check waits for the first store refresh so a renewal that happened while the app was closed is not treated as a lapse.

## Storing

`ReminderEntity.DaysMask` bit 0 is Monday. The editor shows weekdays in the culture's first-day-of-week order.
