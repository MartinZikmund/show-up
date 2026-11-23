# Storage

Where Show Up keeps its data, and whether it survives a restart on each head.

## What is stored where

One SQLite file, `showup.db`, in `ApplicationData.Current.LocalFolder`, opened through
`ShowUpDatabase` (sqlite-net). Six repositories share it, all on `SqliteRepositoryBase` (user scoping,
stamping `Id`/`UserId`/`DeviceId`/timestamps, tombstones):

- `ITrackingRepository` / `SqliteTrackingRepository`: enrollments, completions, shifts, bonus workouts.
- `INotesRepository` / `SqliteNotesRepository`: program, day and session notes.
- `IMetricsRepository` / `SqliteMetricsRepository`: logged results.
- `IReminderRepository` / `SqliteReminderRepository`: daily reminders (minute of day, weekday mask, enabled). No natural key.
- `ICustomProgramRepository` / `SqliteCustomProgramRepository`: user-built programs, one row per revision, natural key `c:{programId}` (`custom-<guid>`). The highest live revision is current (revision = max(latest + 1, seconds since 2026-01-01 UTC), so two offline devices practically never pick the same number); older ones stay (not tombstoned) so enrollments pinned to them still resolve. Delete only writes the synced setting `custom-deleted:{programId}` (UTC timestamp) and keeps every revision: the list and "latest" lookups hide the program when its newest `UpdatedAt` is at or before the marker (an unreadable marker also counts as deleted), but a lookup with an explicit revision ignores the marker so enrollments that ran it keep their history. Saving to a deleted id throws (a new program needs a new id); deleting an unknown id does nothing.
- `ISyncedSettings` / `SqliteSyncedSettings`: key/value settings that follow the user, natural key `k:{key}`, upserted.

| Head | Location | Persistence |
|---|---|---|
| Windows (packaged) | package `LocalState` | not verified |
| Desktop (Skia) | per-user app data folder | survives restarts (verified) |
| WebAssembly | Emscripten IDBFS (`/local` IndexedDB database) | survives reloads (verified, see below) |
| Android / iOS | app sandbox | not verified yet |

## Migrations

`Migrations.ApplyAsync` runs on first connection and tracks the schema with `PRAGMA user_version`.
Add a new numbered step there; never edit a shipped one.

| Version | Change |
|---|---|
| 1 | Enrollments, completions, schedule shifts, bonus workouts |
| 2 | `CompletionEntity.InkJson`: the user's drawn strokes for a cross (see [marking.md](./marking.md#draw-it-yourself)) |
| 3 | `NoteEntity` (program, day and session notes) and `MetricEntryEntity` (logged results), see [results.md](./results.md) |
| 4 | `ReminderEntity`, `CustomProgramEntity` and `SyncedSettingEntity` (frozen DDL, one transaction) |
| 5 | `sync_state` (pull cursors per table and account) and `SyncPending` on every synced table, existing rows pending; see [sync.md](./sync.md) |
| 6 | `catalog_cache_state` and `catalog_cache_program`: the newest downloaded catalog and every program revision ever downloaded, see [catalog-delivery.md](./catalog-delivery.md) |

## WebAssembly: the async file system catch

The WASM file system is IDBFS: IndexedDB mirrored into memory. The load is asynchronous, so a
file opened too early sees an empty folder and quietly creates a fresh database. In our first
run the row count was 1 after every reload (new empty DB each time).

Fix: `ShowUpDatabase` takes an optional `prepareStorage` callback, awaited before opening the
file. `App.RegisterServices` passes (browser only) a `LocalFolder.CreateFolderAsync("db", OpenIfExists)`
call purely to await IDBFS init, because any `StorageFolder` async call waits for the IDBFS load
([Uno docs](https://platform.uno/docs/articles/features/file-management.html#webassembly-file-system)).
No `WasmShellEnableIDBFS` property or explicit flush was needed, and no JSON fallback.

Result with the fix (browser console, one row added per page load): `bonus rows = 2`, then `3`
after reloads, with the app served by `dotnet run -f net11.0-browserwasm`.

Caveat: this was checked with a normal reload, not a hard tab kill right after a write. IDBFS
syncs asynchronously, so a write made in the last moments before the tab closes may be lost.

The database file itself lives at `LocalFolder/showup.db`; the `db` folder is just a throwaway await.

## Debug check

The DEBUG-only "bonus rows" panel used for this spike was removed when the real shell landed
(Today replaced the template's main page). To re-check persistence, log a bonus workout from the app
and reload.

## Server merge for keyed rows

Settings (`k:{key}`), custom programs (`c:{programId}` + `Revision`) and notes/results are matched on their natural key with last writer wins, not unique constraints: two offline devices can each create a row for the same key. The order is `UpdatedAt`, then `Id` (ordinal text of the "D" format), so every device picks the same winner on a tie. Keys are scoped per `UserId`: the same key under two users is two rows. Local reads already take the newest live row per key, and the next write tombstones the rest.

Reminder values are validated on write: `MinutesOfDay` 0-1439, `DaysMask` 1-127.
