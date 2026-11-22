# What syncs and what stays on the device

Accounts and sync need to know, for every piece of state, whether it follows the user or belongs to this device.
When you add a setting or a table, add it here first.

## Settings

| Setting | Where | Scope |
| --- | --- | --- |
| `DataVersion`, `FirstStart`, `LaunchCount`, `OfferUserRating` | `IAppPreferences` | per device |
| `LastKnownPro`, `LastKnownProExpires` | `IAppPreferences` via `AccountProCache` | per device, per account (key carries a hash of the user id) |
| `DebugSimulateSignIn`, `DebugSimulatedSession` | `IPreferences` | per device, debug builds only |
| `LastSyncedAt` | `IPreferences` via `SyncService` | per device, per account (key carries a hash of the user id) |
| `SelectedEnrollmentId` | `IAppPreferences` | per device, per account (key carries a hash of the user id): each device's Calendar can show a different program |
| `Theme`, `MarkerSoundEnabled`, `MarkerHapticsEnabled`, `CrossMomentMode` | `IAppPreferences` | per device |
| Notification permission state | the OS | per device |
| Android `planned-reminders.json` | app files | per device (what this device scheduled with the OS) |
| `primary-enrollment` | `ISyncedSettings` | synced |
| `custom-deleted:{programId}` | `ISyncedSettings` | synced |
| `MarkerColor`, `CrossOffStyle`, `UnitSystem` | `ISyncedSettings` through `SyncedPersonalization` | synced |

`MarkerColor`, `CrossOffStyle` and `UnitSystem` are still read and written through `IAppPreferences` (so callers did not change),
but `AppPreferences` hands them to `SyncedPersonalization`: reads come from memory, writes persist in order in the background,
and `DataChangedMessage` reloads them after a sync pull. The first start after the upgrade moves the old per-device values
into synced settings and removes the old keys.

## Rows

All of these are synced (they carry `UserId`, `DeviceId`, timestamps and tombstones, see [storage.md](./storage.md)):
reminders, custom programs (every revision), notes, metrics (logged results), completions, enrollments, schedule shifts and bonus workouts.

Not synced: the bundled catalog (it ships with the app; over-the-air updates are cached separately, see [catalog-delivery.md](./catalog-delivery.md)).
