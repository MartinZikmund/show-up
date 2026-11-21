# Supabase

The server side of accounts, sync and over-the-air catalog updates: a Postgres schema that mirrors the
local SQLite tables, row level security that keeps every user's rows to themselves, and the published
catalog that anyone can read.

## Layout

```
supabase/
  config.toml                         # local stack (supabase init, CLI v3)
  migrations/<timestamp>_initial.sql  # schema, RLS, grants, triggers
  tests/*_test.sql                    # pgTAP, run by `supabase test db`
.github/workflows/supabase.yml        # CI: start, reset, test, lint, advisors
```

Migrations are hand-written (imperative, no `supabase/schemas/`). Create a new one with
`npx supabase@3 migration new <name>` (no Docker needed) and never edit a migration that has shipped.

`config.toml` sets `auto_expose_new_tables = false`, matching hosted projects since 2026-10-30: a table
is invisible to the Data API until a migration grants it. Seeding is off; the tests create their own rows.

## Running it locally

Needs Docker. Use the CLI through `npx supabase@3` (or an installed v3); v3 targets the local stack
unless you pass `--remote`, so none of these touch a hosted project.

```bash
npx supabase@3 start -x realtime,storage-api,imgproxy,mailpit,studio,edge-runtime,logflare,vector,supavisor
npx supabase@3 db reset          # re-applies every migration from scratch
npx supabase@3 test db           # pgTAP
npx supabase@3 db lint --fail-on error
npx supabase@3 db advisors --type all --fail-on error
npx supabase@3 stop --no-backup
```

Drop the `-x` list to get Studio and the rest of the stack.

## CI

`.github/workflows/supabase.yml` runs on pull requests and pushes to `main` that touch `supabase/**`
(or the workflow). It installs the CLI with `supabase/setup-cli@v3` pinned to `3.2.0`, starts the stack
without the services the database doesn't need, then `db reset`, `test db`, `db lint` and
`db advisors`. Any failing test, lint error or advisor error fails the job. No secrets are involved.

## Tables

Every local syncable table (storage v4) has a server twin; names and columns are snake_case.

| Local (SQLite) | Server (`public`) | Natural key index |
|---|---|---|
| `EnrollmentEntity` | `enrollments` | |
| `CompletionEntity` (incl. `InkJson`) | `completions` | |
| `ScheduleShiftEntity` | `schedule_shifts` | |
| `BonusWorkoutEntity` | `bonus_workouts` | |
| `NoteEntity` | `notes` | `(user_id, subject_key)` |
| `MetricEntryEntity` | `metric_entries` | `(user_id, subject_key, metric_id)` |
| `ReminderEntity` | `reminders` | |
| `CustomProgramEntity` | `custom_programs` | `(user_id, subject_key, revision)` |
| `SyncedSettingEntity` | `synced_settings` | `(user_id, subject_key)` |

Natural key indexes are deliberately **not unique**: two offline devices can each create a row for the
same key, and the clients pick the winner (see [storage.md](./storage.md#server-merge-for-keyed-rows)).
There are no foreign keys between user tables either, because each table syncs on its own and rows can
arrive in any order. Only `user_id` references `auth.users` (`on delete cascade`: deleting the account
deletes its rows).

Catalog tables (written by the publish job, read by the app):

| Table | Columns |
|---|---|
| `catalog_versions` | `version int` primary key, `published_at` |
| `catalog_workouts` | `id text` primary key, `json jsonb`, `version int` |
| `catalog_programs` | `id text`, `revision int`, `json jsonb`, `version int`; primary key `(id, revision)`, every revision kept |

### Column mapping

| Local column | Local type | Server column | Server type | Client conversion |
|---|---|---|---|---|
| `Id` | `Guid` (varchar 36) | `id` | `uuid` primary key | the same GUID, generated on the device |
| `UserId` | `string?` | `user_id` | `uuid not null` | rows with a null `UserId` (signed out, not claimed) are never pushed |
| `DeviceId` | `string` | `device_id` | `text not null` | as is |
| `CreatedAt`, `UpdatedAt` | `DateTimeOffset` (UTC ticks) | `created_at`, `updated_at` | `timestamptz not null` | ISO 8601 UTC; see precision below |
| `DeletedAt`, `EndedAt` | `DateTimeOffset?` | `deleted_at`, `ended_at` | `timestamptz` | null stays null; `deleted_at` is the tombstone |
| `CompletedAt` | `DateTimeOffset` | `completed_at` | `timestamptz not null` | as `CreatedAt` |
| `*DayNumber` (`StartDayNumber`, `CompletedOnDayNumber`, `FromDayNumber`, `DayNumber`) | `int` | `start_day_number`, ... | `int` | unchanged: `DateOnly.DayNumber` (days since 0001-01-01), not a `date` |
| `EndReason` | `EnrollmentEndReason?` | `end_reason` | `int` | enum value: 0 Finished, 1 Abandoned |
| `Enabled` | `bool` (integer) | `enabled` | `boolean` | |
| `Value` | `double?` | `value` | `double precision` | |
| `InkJson`, `ProgramJson`, other strings | `string` | `ink_json`, `program_json`, ... | `text` | as is; JSON stays `text` so it round-trips byte for byte |
| - | - | `server_updated_at` | `timestamptz not null` | set by the server only, see below |

Integer and string columns keep their local names in snake_case (`DayIndex` → `day_index`,
`MinutesOfDay` → `minutes_of_day`, `SubjectKey` → `subject_key`, ...). `reminders` keeps the local
validation as check constraints: `minutes_of_day` 0-1439, `days_mask` 1-127.

**Timestamp precision:** sqlite-net stores `DateTimeOffset` as 100 ns ticks, Postgres keeps
microseconds. A value with a non-zero last tick digit comes back from the server 0-900 ns earlier than
the device wrote it, which would make "same row, same `UpdatedAt`" compare unequal. The client should
stamp `CreatedAt`/`UpdatedAt`/`DeletedAt` truncated to whole microseconds, so both sides hold the same value.

### `server_updated_at`, the pull cursor

A `BEFORE INSERT OR UPDATE` trigger (`private.set_server_updated_at`) sets it to `now()` on every write,
whatever the client sent. Clients pull with `server_updated_at > cursor` and never compare it with
device clocks. Each table has an index on `(user_id, server_updated_at, id)`.

Two things for the sync engine:

- `now()` is the transaction start time, so all rows of one upsert batch share a timestamp. Page with
  `(server_updated_at, id)` as the cursor, not `server_updated_at` alone, or a page break inside a
  batch skips rows.
- A transaction that started earlier can commit after a later one, so a row can appear with a
  timestamp just below the cursor. Pull with a small overlap (re-read the last few seconds); applying
  a row twice is harmless under last writer wins.

### Last writer wins on the server

A second `BEFORE UPDATE` trigger (`private.keep_newest_row`) keeps the stored row when an update's
`updated_at` is older than the stored one: a device that pushes a stale copy can't overwrite a newer
row. The write doesn't fail (the rest of the batch goes through); the trigger returns the stored row
unchanged, and `set_server_updated_at` still bumps `server_updated_at`, so every device, the stale one
included, pulls the winning row again on its next sync.

- **Equal `updated_at` is accepted**, so re-pushing the same row is idempotent. The flip side: if two
  devices write the same row with the same `updated_at`, the last one to arrive wins.
- **A far-future `updated_at`** (a device with a badly wrong clock) freezes that row: no correctly
  clocked write can beat it until real time passes it. Only the user's own row is affected (RLS keeps
  it inside their account), so a device can only hurt itself.

Hand-off for the sync engine (Task 4): stamp a row's local `UpdatedAt` monotonic per row,
`max(now, previous + 1 µs)`, truncated to whole microseconds. An edit then always beats the version it
replaces, even when the clock steps back or two edits land in the same microsecond.

## Security model

- **RLS on every table in `public`.** A schema guard test fails if a table without RLS appears.
- **User tables:** `SELECT`, `INSERT` and `UPDATE` policies `TO authenticated` with
  `(select auth.uid()) = user_id` in `USING` and (for `INSERT`/`UPDATE`) `WITH CHECK`, so a user can
  neither see, change, create nor take over (by reassigning `user_id` or upserting someone else's
  `id`) another user's rows. `(select ...)` makes Postgres evaluate `auth.uid()` once per statement.
- **No deletes.** There is no `DELETE` policy and no `DELETE` grant: rows are tombstoned with
  `deleted_at`. Account deletion removes rows through the `auth.users` cascade.
- **Explicit grants.** Migrations `revoke all` and then grant: `authenticated` gets
  `select, insert, update` on user tables; `anon` and `service_role` get nothing on user tables;
  catalog tables are `select` for `anon` and `authenticated`, and full access for `service_role` only
  (the publish job). This works whether or not the project auto-exposes new tables.
- **No privileged functions in exposed schemas.** Trigger functions live in the `private` schema
  (not in `api.schemas`), are `SECURITY INVOKER` with an empty `search_path`, and have `EXECUTE`
  revoked from `public`, `anon` and `authenticated`.
- **The app ships the publishable key only.** Never the secret/`service_role` key. Authorization never
  reads `user_metadata`.

## Tests

`supabase/tests/` (pgTAP, each file in a transaction that rolls back):

- `user_tables_rls_test.sql`: for each of the nine user tables, as user A against user B's row: can't
  read, update, insert as B, upsert-take-over or reassign `user_id`; can't delete own rows; anon can't
  read or insert; B's row is unchanged; `server_updated_at` is the server clock on insert and advances on
  update; an equal `updated_at` is accepted; an older one leaves the stored row unchanged but still
  advances `server_updated_at`; a newer tombstone goes through.
- `catalog_test.sql`: anon and authenticated read all three catalog tables; neither can insert,
  update or delete; the service role can write.
- `schema_security_test.sql`: the exact table list, RLS everywhere, no `DELETE`/`ALL` policies, no
  user-table privileges for `anon`/`service_role`, no `DELETE`/`TRUNCATE` for `authenticated`, no
  `SECURITY DEFINER` in `public`, the trigger on every user table, every user-table policy
  `TO authenticated`, `auth.uid()` in `USING` (SELECT/UPDATE) and in `WITH CHECK` (INSERT/UPDATE).

## Hosted project

Not created yet. Linking a project, pushing migrations (`--remote` and an explicit confirmation in CLI
v3), keys and the custom SMTP requirement are covered when the catalog publish job lands.
