# Show Up — Plan 4: Supabase (auth, sync, catalog API) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Optional sign-in, cross-device sync of all user data (Pro), and the full workout catalog served from Supabase with over-the-air updates — all of it inert until Martin plugs in a project URL + publishable key.

**Architecture:** Schema-in-repo (`supabase/` with migrations, RLS, seed) applied locally with the Supabase CLI for tests. The app talks to Supabase through a **thin, AOT-safe REST client** over `HttpClient` + System.Text.Json source generation (GoTrue auth endpoints, PostgREST, Storage public URLs) — no community SDK, so WASM trimming stays safe and the dependency surface stays tiny. Sync is row-based: push rows with `UpdatedAt > lastPush`, pull rows with `server_updated_at > cursor`, last-write-wins on `UpdatedAt` for mutable rows, tombstones never hard-deleted.

**Tech Stack:** Supabase (Postgres 17, Auth, PostgREST, Storage), Supabase CLI v3 via `npx supabase@3` (local by default), Docker only in CI for the local stack, .NET 11 HttpClient + STJ source-gen.

**Spec:** `docs/superpowers/specs/2026-11-21-show-up-design.md` (Sync, Account switching, Catalog delivery).

## Global Constraints

- All earlier plans' global constraints apply.
- **Supabase specifics (from the supabase skill + changelog 2026-11):** RLS enabled on every table in `public`; policies `TO authenticated` **with** `(select auth.uid()) = user_id` in `USING` and `WITH CHECK` (UPDATE needs a SELECT policy too); explicit `GRANT`s for `authenticated` (user tables) and `anon` (catalog read-only) since new tables may not be auto-exposed; no `SECURITY DEFINER` in exposed schemas; never `user_metadata` for authz; **publishable key** in the app (never service_role/secret); CLI v3 is local-by-default — CI and scripts must pass explicit flags to touch hosted; Free-plan default SMTP can't customise templates → document that production needs custom SMTP.
- **Auth UX:** email **one-time code** (6 digits via `signInWithOtp` + `verifyOtp` type `email`) — works on every head without deep links; Apple/Google OAuth deferred. Sign-in is optional; sync is Pro (`FeatureGate.CanUse(CloudSync)`).
- Config: `Supabase:Url`, `Supabase:PublishableKey` (appsettings; empty → all Supabase features hidden, app fully offline). Tokens stored via a platform secure store (Windows `PasswordVault`/DPAPI, Android Keystore-backed prefs, iOS Keychain, WASM `localStorage` with a documented caveat, desktop DPAPI on Windows / file with 0600 on macOS/Linux).
- Account switching (spec): first sign-in asks "Bring your local progress into this account?" — yes claims `UserId = null` rows; data of different accounts never mixes.

- **Plan 3 hand-offs (binding):** use `IEntitlementService.SetUserAsync(userId)` after sign-in / `null` after sign-out (RevenueCat `LoginAsync`/`LogOutAsync`, Pro cache keyed by account); after every sync pull, claim, user switch and OTA catalog swap send `DataChangedMessage` (Core) — `ActiveEnrollmentService`/`CatalogService` `Invalidate()` and LiveRefresher VMs + `ReminderCoordinator` re-plan react to it; flip the paywall's Cloud sync row from "Coming soon" to available via the ProFeature availability flag when sync ships; sync only what `docs/sync-classification.md` marks as synced (rows + `ISyncedSettings`), never per-device prefs; server merge per `docs/storage.md` (LWW on natural keys scoped by `user_id`, `(user_id, subject_key)` indexes, never plain unique constraints on natural keys); account entitlements (later) compose with store entitlements in one `IEntitlementService` raising `Changed` once; catalog workout ids are append-only (`catalog/workout-ids.lock`) — the publish job must refuse removals.
- **Carried from Plan 3 review (do in Task 3/4):** `SetUserAsync` runs inside the entitlement refresh single-flight and is atomic (seed the new account's Pro cache from the anonymous one until the store confirms); `LapseCoordinator` prompts only on a store-confirmed lapse (`IsConfirmed`); `SyncedPersonalization` reload falls back to the current in-memory value and re-applies the marker brush; `LiveRefresher` DataChanged handling must not depend on messenger handler order (await the services' invalidation explicitly); the sync engine sends ONE coalesced `DataChangedMessage` per pull.
- **Local Supabase stack (ruling):** Docker Desktop is not started on Martin's machine. Migrations/RLS/pgTAP/advisors run against the local stack in CI (`supabase start` on GitHub runners); locally, write SQL + C# with fakes, and use `npx supabase@3` (CLI v3 is local-by-default) only for commands that don't need Docker.

## Review Focus

1. **Two devices cross the same session offline, then sync** — both rows arrive; UI shows one cross; uncross on one device tombstones both after the next sync. Test in Task 4 (two in-memory "devices" against a fake server).
2. **Clock skew between devices** (device clock 10 min ahead) — pull uses the server cursor, not device time; LWW uses `UpdatedAt` but ties broken by `Id`. Test in Task 4.
3. **Token expiry mid-sync** — refresh once, retry once, then surface "Sign in again" without losing unsent rows. Test in Task 3/4.
4. **Sign out with unsynced rows** — warn and offer "Sync now"; never delete local rows. Test in Task 5.
5. **OTA catalog with a program that fails validation** — skipped, bundled version kept, warning logged. Test in Task 6.

---

### Task 1: Schema, RLS and local Supabase project (`supabase/`)
- `supabase init` structure committed (`supabase/config.toml`, `supabase/migrations/<ts>_initial.sql` created via `supabase migration new`), tables mirroring local storage: `enrollments`, `completions` (incl. `ink_json`), `schedule_shifts`, `bonus_workouts`, `custom_programs`, `reminders`, `synced_settings`, `notes`, `metric_entries`, each with `id uuid pk`, `user_id uuid not null references auth.users`, `device_id`, `created_at`, `updated_at`, `deleted_at`, `server_updated_at timestamptz not null default now()` + trigger setting `server_updated_at = now()` on insert/update; index `(user_id, server_updated_at)`.
- Catalog tables: `catalog_versions(version int pk, published_at)`, `catalog_workouts(id text pk, json jsonb, version int)`, `catalog_programs(id text, revision int, json jsonb, version int, primary key (id, revision))`; `anon` + `authenticated` SELECT only.
- RLS policies per the constraints; no DELETE policies on user tables (tombstones only).
- SQL tests with pgTAP (`supabase/tests/*.sql`, `supabase test db`): user A can't read/update B's rows; anon can read catalog, not user tables; update can't reassign `user_id`.
- CI job (`.github/workflows/supabase.yml`): start local stack (`supabase start`), `supabase db reset`, `supabase test db`, `supabase db advisors` — fails on advisor errors.
- [ ] Commit, PR `Refs #17`.

### Task 2: Thin Supabase client (Core) + secure token store (head)
- Core `Supabase/SupabaseOptions.cs`, `ISupabaseAuth` (`SendCodeAsync(email)`, `VerifyCodeAsync(email, code)`, `RefreshAsync()`, `SignOutAsync()`, `Session? Current`, `event SessionChanged`), `ISupabaseRest` (`UpsertAsync<T>(table, rows)`, `SelectSinceAsync<T>(table, cursor, limit)` with `Prefer: resolution=merge-duplicates`, `return=minimal`), JSON via `SupabaseJsonContext`; `ITokenStore` in Core, platform impls in head.
- Tests against a fake `HttpMessageHandler`: request shapes (headers `apikey`, `Authorization: Bearer`), refresh-on-401-once, error mapping.
- [ ] Commit, PR.

### Task 3: Account UI and local-data claim
- Settings → Account card: signed out (email box → "Send code" → 6-digit code entry), signed in (email, sync status, last synced, "Sync now", "Sign out"). Claim dialog on first sign-in; repositories switch `IUserScope.CurrentUserId` live.
- Tests: `SignIn_FirstTime_AsksToClaim`, `Claim_Yes_SetsUserIdOnAnonymousRows`, `SignIn_DifferentAccount_DoesNotUploadOthersRows`, `SignOut_UnsyncedRows_Warns`.
- [ ] Commit, PR `Refs #17`.

### Task 4: Sync engine
- Core `Sync/SyncEngine.cs`: per table push (`UpdatedAt > lastPush[table]`, batches of 200) then pull (`server_updated_at > cursor[table]`, page 500, apply LWW by `UpdatedAt` then `Id`), cursors in local `sync_state`; triggers: app start, resume, debounced 5 s after local change, manual. Pro-gated; lapse → paused (spec).
- Tests with two simulated devices over a fake server: Review Focus 1–3, tombstone propagation, cursor monotonicity, partial failure resumes.
- [ ] Commit, PR `Closes #17`.

### Task 5: OTA catalog
- `CatalogService` gains a remote source: on start (online) fetch `catalog_versions` max; if > bundled/cached → download workouts + programs for that version, validate each (TryParseProgram + validator), cache JSON in local storage, swap atomically; keep pinned revisions available (enrollments resolve their revision from cache or bundled).
- Tests: Review Focus 5, `Remote_NewerVersion_Replaces`, `Remote_Offline_UsesCached`, `PinnedRevision_StillResolvable`.
- [ ] Commit, PR `Refs #18`.

### Task 6: Catalog publishing workflow
- `.github/workflows/catalog-publish.yml` on push to `main` touching `catalog/**`: validate (Core tests), then upsert into Supabase via a small script (`tools/publish-catalog.py` or .NET tool) using `SUPABASE_URL` + `SUPABASE_SECRET_KEY` repo secrets — **skips with a notice when secrets are absent**; bumps `catalog_versions`.
- Hero images: `catalog-heroes` Storage bucket (public read), `heroImage` = public URL; bundled fallbacks remain.
- `docs/supabase.md`: creating the project, applying migrations (`supabase link` + `supabase db push` with explicit hosted confirmation in CLI v3), keys to put where, custom SMTP note, scoped access tokens for CI.
- [ ] Commit, PR `Closes #18`.

## Following plan
5. Hero art, history/stats, celebrations, accessibility pass, store metadata, release.
