# Supabase

The server side of accounts, sync and over-the-air catalog updates: a Postgres schema that mirrors the
local SQLite tables, row level security that keeps every user's rows to themselves, and the published
catalog that anyone can read.

## Layout

```
supabase/
  config.toml                         # local stack (supabase init, CLI v3)
  migrations/<timestamp>_initial.sql  # schema, RLS, grants, triggers
  migrations/<timestamp>_catalog_heroes_bucket.sql  # public Storage bucket for program hero images
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
npx supabase@3 start -x realtime,imgproxy,mailpit,studio,edge-runtime,logflare,vector,supavisor
npx supabase@3 db reset          # re-applies every migration from scratch
npx supabase@3 test db           # pgTAP
npx supabase@3 db lint -s public,private --fail-on error
npx supabase@3 db advisors --type all --fail-on error
npx supabase@3 stop --no-backup
```

Drop the `-x` list to get Studio and the rest of the stack.

## CI

`.github/workflows/supabase.yml` runs on pull requests and pushes to `main` that touch `supabase/**`
(or the workflow). It installs the CLI with `supabase/setup-cli@v3` pinned to `3.2.0`, starts the stack
without the services the database doesn't need (Storage stays: the hero bucket lives in its schema), then `db reset`, `test db`, `db lint` and
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
validation as check constraints: `minutes_of_day` 0-1439, `days_mask` 1-127. Free-text and JSON columns are
capped in bytes (`octet_length`): `completions.ink_json` 64 KB, `custom_programs.program_json` 256 KB, `notes.text`
16 KB and `metric_entries.text_value` 4 KB. The app stays below each (ink 600 points, notes 4000 chars, metric
text 200 chars, and the schedule builder refuses a program over 256 KB).

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
- `catalog_heroes_bucket_test.sql`: the `catalog-heroes` bucket is public, 2 MiB, webp/png/jpeg only; no
  `storage.objects` policy for `anon`/`authenticated`; neither can upload or list.
- `schema_security_test.sql`: the exact table list, RLS everywhere, no `DELETE`/`ALL` policies, no
  user-table privileges for `anon`/`service_role`, no `DELETE`/`TRUNCATE` for `authenticated`, no
  `SECURITY DEFINER` in `public`, the trigger on every user table, every user-table policy
  `TO authenticated`, `auth.uid()` in `USING` (SELECT/UPDATE) and in `WITH CHECK` (INSERT/UPDATE).

## The client

A small client in `ShowUp.Core/Supabase/`, no community SDK: `HttpClient` plus `System.Text.Json` source
generation, so it is trim and AOT safe. It only knows what the app needs.

- `SupabaseOptions` is bound from `Supabase:Url` and `Supabase:PublishableKey` (`appsettings.json`, both
  empty in the repo). Empty means `IsConfigured` is false: calls throw `SupabaseException` with kind
  `NotConfigured` and the UI should hide every Supabase feature. Only the publishable key (`sb_publishable_...`)
  ever ships; it goes in the `apikey` header, never as a bearer token.
- `ISupabaseAuth` is GoTrue email codes: `SendCodeAsync` (`POST /auth/v1/otp`, `create_user: true`),
  `VerifyCodeAsync` (`POST /auth/v1/verify`, `type: "email"`), `RefreshAsync`
  (`POST /auth/v1/token?grant_type=refresh_token`), `SignOutAsync` (`POST /auth/v1/logout?scope=local`, best effort,
  the local session is always cleared). The Magic Link and Confirm signup email templates must include `{{ .Token }}` or
  users get a link instead of a code. `Current` is the `Session`; `SessionChanged` fires on sign-in, refresh and sign-out.
- Refresh is proactive (the access token expires within 60 s) and reactive (a 401 refreshes once and retries once).
  Concurrent callers share one refresh (a caller that cancels only stops waiting). Only a real GoTrue rejection
  signs the device out: HTTP 400, 401 or 403 with `error_code` `refresh_token_not_found`, `refresh_token_already_used`,
  `session_not_found`, `session_expired`, `bad_jwt`, `user_not_found` or `user_banned` (or `error: invalid_grant`).
  Anything else (network, timeout, 5xx, a proxy's 403) keeps the session and throws. A refresh result is applied
  only if the session it started from is still current, so a sign-out or a new sign-in during a refresh wins.
  `SessionChanged` is raised off the UI thread and not by `RestoreAsync`. `SupabaseOptions.IsConfigured` accepts
  https, or http only for `localhost` / `127.0.0.1` (the local stack).
- `ISupabaseRest` is what the sync engine uses. Rows go in and out as `JsonTypeInfo<T>` from a source-generated
  context, so no reflection: `UpsertAsync` posts to `/rest/v1/{table}?on_conflict=id` with
  `Prefer: resolution=merge-duplicates,return=representation` and returns the stored rows (a stale update comes
  back as the winning row). `SelectPageAsync` orders by `server_updated_at.asc,id.asc` with a `limit`; with a cursor
  only it reads `server_updated_at=gte.<cursor>` (the overlap), with the last row's id too it resumes strictly after
  that `(server_updated_at, id)`. Timestamps are sent as UTC, truncated to microseconds. No `user_id` filter: RLS
  scopes the rows. Row records that have an `Id` property must be `partial` (Uno's key-equality analyzer, KE0001).
- Failures are `SupabaseException` with a `Kind` (`Network`, `InvalidCode`, `Unauthorized`, `RateLimited` with
  `RetryAfter`, `Validation`, `Server`, ...), the HTTP status and the server's error code. It never contains tokens,
  and nothing in the client logs them.

### Where the session lives

`ITokenStore` holds one string, the serialized session (access and refresh token). The head picks the store:

| Head | Store | Notes |
|---|---|---|
| Windows (packaged or not); Desktop on Windows | `FileTokenStore` in `LocalFolder` + DPAPI | current user, this machine only; no roaming and no shared locker. `LocalFolder` is already per-identity, so worktree builds don't share a session |
| Desktop on macOS and Linux | `FileTokenStore` in `LocalFolder`, plain | created `0600` (owner only); no keychain integration yet |
| Android | `FileTokenStore` + AES-GCM with a non-exportable Android Keystore key | `EncryptedSharedPreferences` needs an extra AndroidX package and is deprecated; a restored backup has no key and reads as signed out |
| iOS | Keychain (`SecKeyChain`) | `AfterFirstUnlockThisDeviceOnly`; Keychain items survive an uninstall, so a reinstall can find the old session |
| WebAssembly | `localStorage` | **XSS caveat:** any script on the origin can read it; the browser has no secure store. Load no third-party scripts |

A file or keychain entry that cannot be decrypted reads as "signed out", never as an error.

## Going live

Nothing in the repo touches a hosted project by itself. These are the steps you do once, by hand.

1. **Create the project** in the Supabase dashboard (Free plan is fine to start). Note the project ref
   (20 lowercase letters, in the dashboard URL) and set the database password.
2. **Link and push the schema.** CLI v3 is local by default, so hosted commands need `--remote` and an
   explicit confirmation:

   ```bash
   npx supabase@3 login                      # or SUPABASE_ACCESS_TOKEN with a scoped token
   npx supabase@3 link --project-ref <ref>
   npx supabase@3 db push --remote --dry-run # lists the migrations it would apply
   npx supabase@3 db push --remote           # asks for confirmation; --yes (or SUPABASE_YES) skips it
   ```

   This applies every migration, including the `catalog-heroes` bucket. `db push` connects with the database
   password (`--password` or `SUPABASE_DB_PASSWORD`), so the token's scope doesn't limit it.
3. **Data API.** The migrations grant explicitly, so they work either way, but keep *Automatically expose new
   tables* off (Dashboard, Integrations, Data API, Settings) to match `config.toml`. Then check that
   `anon`/`authenticated` can read the catalog tables and nothing else.
4. **Auth settings** (Authentication in the dashboard):
   - Email provider on. Sign-in is a code: put `{{ .Token }}` in **both** the **Magic Link** template (returning
     users) and the **Confirm signup** template (new users get that one while email confirmation is on); see
     [accounts.md](./accounts.md). Verify in the dashboard, then by signing up a fresh address and checking the
     mail shows a code, not just a link.
   - Email OTP length 6 (the app's code field expects six digits) and a short *Email OTP expiration* (the
     default is 1 hour; shorter is safer).
   - **Custom SMTP.** The built-in sender only mails project team members and is limited to 2 messages an
     hour, so real users never get a code. Add an SMTP provider (Authentication, Emails, SMTP Settings), then
     raise the rate limits to match. Set this up before the first public build.
5. **Keys for the app.** Put the project URL and the **publishable** key (`sb_publishable_...`) in
   `appsettings.json` as `Supabase:Url` and `Supabase:PublishableKey` (or the build's configuration source).
   The host of `Supabase:Url` must equal the host of the `SUPABASE_URL` secret: the app only loads hero images
   from its own Supabase host, so a mismatch drops every remote hero.
   Never the secret key. Empty values keep every Supabase feature hidden.
6. **Keys for CI.** In GitHub, Settings, Environments, create `catalog`, set *Deployment branches* to `main` only (the
   workflow also refuses to publish from any other branch), and add required reviewers if you want a
   manual gate on every publish. Then add the secrets `SUPABASE_URL` and `SUPABASE_SECRET_KEY`
   (`sb_secret_...`, from Settings, API Keys; create a dedicated one so it can be rotated alone). Without them
   the publish workflow just prints a notice. Any CLI token CI ever needs must be a scoped access token
   limited to this project, never a classic one.
7. **First publish.** Run the *Catalog publish* workflow by hand with *dry run* ticked, read the plan, then run
   it again without. Storage uploads are only exercised by the first publish that carries a hero image, so treat that one
   as the real test of the bucket: check the image opens at its public URL. See [catalog-delivery.md](./catalog-delivery.md#publishing).

The secret key maps to the `service_role` role and bypasses RLS; the grants still apply, which is why the
catalog tables give it write access and the user tables give it none.
