# Sync

How a signed-in Pro user's rows travel between this device and Supabase. The local SQLite database stays the
source of truth: every screen reads and writes it, and sync runs in the background. Sync is a Pro feature
(`FeatureGate.CanUse(ProFeature.CloudSync)`); signed out, without Pro, or in a build with no Supabase project it
does nothing and deletes nothing.

Whether a build offers sync at all is one answer, `CloudSyncAvailability` (registered in `App`: `SupabaseOptions.IsConfigured`,
or the debug simulated sign-in). `FeatureGate.IsAvailable(ProFeature.CloudSync, availability)` reads it for both the
paywall ("Coming soon" without a project) and `SyncService`, so they never disagree.

Server side (tables, RLS, `keep_newest_row`, `server_updated_at`): [supabase.md](./supabase.md). What syncs at all:
[sync-classification.md](./sync-classification.md). Accounts and claiming signed-out rows: [accounts.md](./accounts.md).

## The pieces

| Type (`ShowUp.Core`) | Job |
|---|---|
| `Sync/SyncEngine` | one pass for one account: per table, push then pull |
| `Sync/SyncTable<TEntity, TRow>` | one table's push, pull and last-writer-wins; the entity ↔ row mapping lives in `SyncEngine` |
| `Sync/SyncRows.cs` | the server row classes and `SyncJsonContext` (snake_case, every key written, whole-microsecond UTC timestamps) |
| `Sync/SyncService` | when to sync, one sync at a time, backoff, status; it is the app's `ISyncStatus` |
| `Sync/InMemorySyncServer` | the server's semantics in memory, for tests and the debug build's simulated sign-in |

## Local bookkeeping (storage v5)

- **`SyncPending`** on every synced table (`integer not null default 1`, so rows from before v5 start dirty).
  Every repository write sets it (`SqliteRepositoryBase.Insert`/`InsertAsync`/`Touch`, and the claim); a push the
  server confirms clears it. Push selects `UserId = current AND SyncPending = 1`, so only the signed-in account's
  rows ever leave the device, and claimed settings that keep their old `UpdatedAt` still upload.
- **`sync_state(TableName, UserId, PullCursor, PullLastId)`**: the pull cursor per table and account
  (`server_updated_at` as UTC ticks). `PullLastId` records the id of that row for diagnostics only: every sync starts
  in overlap mode from `PullCursor`, so it is never read back.
- **Stamps**: every write stamps whole microseconds (what Postgres keeps) and is monotonic per row:
  `UpdatedAt = max(now, previous + 1 µs)` (`Timestamps.Next`). An edit always beats the version it replaces, even
  if this device's clock is behind the one that wrote it. `DeletedAt` takes the same stamp as the tombstone's
  `UpdatedAt`.
- **Row contexts serialize every key.** Never use `DefaultIgnoreCondition = WhenWritingNull` on the row
  `JsonSerializerContext`: a batch upsert whose rows have different key sets fails with PostgREST `PGRST102`, and a
  null has to reach the server to clear a column such as `deleted_at`.
- **`ShowUpDatabase.LocalChanged`** fires on every repository write. The engine writes through the connection
  directly, so rows it applies never raise it: a sync can't trigger itself.

## One pass

For each table, in `SyncEngine.Tables` order:

1. **Push.** Pending rows go up in batches of 200 (`UpsertAsync`, `on_conflict=id`, merge-duplicates,
   `return=representation`). Each returned row is the server's stored winner:
   - same `UpdatedAt` as sent → ours is stored; `SyncPending` is cleared (only if the row wasn't edited again
     while the request was out);
   - newer → the server kept another device's version (`keep_newest_row`); it is applied here and the flag cleared;
   - a row edited during the push keeps its flag and goes up next time.
2. **Pull.** The first page reads `server_updated_at >= cursor - 5 s` (the overlap catches a transaction that
   started earlier but committed after the cursor moved on), then keyset pages of 500 by
   `(server_updated_at, id)`. Each page's rows and the new cursor are written in one transaction, so a failure
   on page 3 resumes after page 2. The cursor only moves forward, and only over the account's own rows.

**Account switches.** The session can change accounts at any await (a sign-out, a revoked refresh token). A pass
belongs to one account (`SyncPass`): before every request and inside every write transaction it checks that account
is still signed in, and `SyncService` cancels it on `IUserScope.Changed`. Rows fetched with the next account's token
are never written, the old account's cursor never moves, and its `LastSyncedAt` isn't recorded; the switch already
asked for a sync of the new account.

**One table failing.** If the server rejects one table (validation, RLS, a response that can't be read) the others
still sync; the pass then throws `SyncTablesFailedException` so the status shows the failure and retries. Being offline,
a rejected session or a cancel stops the whole pass at once.

**Last writer wins**, per row id, by `UpdatedAt`:

- a local row with that id that belongs to another account is left alone;
- no local row → insert it;
- remote strictly newer → replace the local row (a pending local edit that is older loses);
- equal `UpdatedAt`, local not pending, content differs → take the server's (equal stamps: last to arrive wins there);
- otherwise keep the local row (a newer pending edit is pushed next time).

Tombstones are ordinary rows with `DeletedAt` set, so deletes propagate the same way.

**Natural keys.** Two offline devices can each create a row for the same thing (the same session crossed twice,
the same setting, the same note). Both rows are kept and synced; reads already pick one per key (completions: the
earliest; notes, results, settings: newest by `UpdatedAt` then `Id`), so the UI shows one cross. Uncrossing
tombstones every live completion of that session, and the next note or setting write tombstones the extra rows.

**Settings seeds.** A synced setting with no value yet is seeded with `UpdatedAt` = 1970-01-01
(`ISyncedSettings.SeedAsync`), so a default written at startup never beats the value the account already has.

## Triggers and states

`SyncService` runs a sync:

| When | Notes |
|---|---|
| app start | after the session is restored (`App.OnLaunched`) |
| resume | window activation, at most once a minute |
| 5 s after a local write | debounced; `LocalChanged` restarts the timer |
| sign-in / account switch | `IUserScope.Changed` |
| Pro regained | `IEntitlementService.Changed` while paused |
| "Sync now" | Settings → Account, and the sign-out prompt; resets the backoff |

One sync runs at a time; a request meanwhile makes it go round once more. A sync that applied remote rows sends one
`DataChangedMessage` through `DataChangeAnnouncer` (on the UI thread): pages reload, `SyncedPersonalization`
re-reads the marker color, cross-off style and units, and `ReminderCoordinator` re-plans.

| `SyncState` | Card text | What happens |
|---|---|---|
| `Off` | (card hidden) | no Supabase project |
| `SignedOut` | | |
| `Paused` | Paused · Sync is a Pro feature | no Pro; rows wait, nothing deleted |
| `Syncing` | Syncing… | |
| `Idle` | Last synced 5 minutes ago | last sync time per account per device (`LastSyncedAt.<hash>` preference) |
| `Offline` | Offline — will retry | network, 5xx or rate limit: retry after 30 s, 1, 2, 5, then every 15 min (or `Retry-After`, capped at 15 min) |
| `SignInAgain` | Sign in again to sync | the server rejected the session after the client's own refresh-and-retry; unsent rows stay pending, no retry |
| `Failed` | Sync failed — will retry | a table the server rejected, or anything else; same backoff |

Signing out with pending rows (`ISyncStatus.HasUnsyncedChangesAsync`, false when sync is off or paused) asks first:
**Sync now** (then signs out if everything went up, otherwise says so and stays), **Sign out anyway**, or **Cancel**.

## Trying it without a hosted project

Debug builds: Settings → Debugging → **Simulate sign-in** also routes sync to an `InMemorySyncServer`
(`Services/Sync/DebugSupabaseRest`), so the status line, Sync now, the paused state (Pro override → Force Free) and
the sign-out prompt work end to end. The fake server forgets everything on restart.

## Tests

`tests/ShowUp.Core.Tests/Sync/` runs two (or more) simulated devices, each with its own SQLite file and clock,
against one `InMemorySyncServer` with a controllable server clock: duplicate offline crosses show one cross and
uncross tombstones both, 10-minute clock skew either way, an account switch mid-pass, a leaked foreign row,
one rejected table, token expiry mid-push keeps unsent rows, tombstones,
stale pushes take the server winner, cursor monotonicity, the late-commit overlap, a failed second page resumes,
only the current user's rows are pushed, claimed settings upload but lose to newer account values, and pulled
rows don't re-trigger a sync. The real database semantics are covered by the pgTAP tests in CI.
