# Storage

Where Show Up keeps its data, and whether it survives a restart on each head.

## What is stored where

One SQLite file, `showup.db`, in `ApplicationData.Current.LocalFolder`, opened through
`ShowUpDatabase` (sqlite-net) and used via `ITrackingRepository` / `SqliteTrackingRepository`.

| Head | Location | Persistence |
|---|---|---|
| Windows (packaged) | package `LocalState` | not verified |
| Desktop (Skia) | per-user app data folder | survives restarts (verified) |
| WebAssembly | Emscripten IDBFS (`/local` IndexedDB database) | survives reloads (verified, see below) |
| Android / iOS | app sandbox | not verified yet |

## Migrations

`Migrations.ApplyAsync` runs on first connection and tracks the schema with `PRAGMA user_version`.
Add a new numbered step there; never edit a shipped one.

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

In DEBUG builds only (the VM members and the panel are compiled out of Release), the main page
shows the bonus row count with an "Add row" button, and the view model logs `ShowUp storage: bonus rows = N` to the console on load.
