# Show Up — Design

**Date:** 2026-11-21 · **Status:** draft for approval · **Repo:** `MartinZikmund/show-up` (public)

## Intent

A home-workout tracker that keeps people accountable. You pick a program (Insanity, Insanity MAX:30,
Insanity Unhinged, P90X, …), and every finished workout is crossed off on a calendar **like drawing an X
with a marker on a paper calendar on the fridge** — with the stroke animation, a marker sound and a haptic
tick. Accountability comes from the visible chain of crosses, reminders, and the ability to log "bonus"
workouts outside the active program without breaking it.

It ships to the stores as a real product under Martin's brand, built in public.

## Goals

- Large, **verified** catalog of program calendars (Bodi programs first, then others).
- The marker-cross moment: animated hand-drawn X, sound, haptics. This is the signature feature.
- **Multiple sessions per day** (e.g. *Cardio Power & Resistance* + *Cardio Abs*), each checked off
  individually. First session draws the first stroke "/", completing the last draws the full X. With 3+
  sessions each gets a small tick and the X lands when the day is complete.
- **Bonus workouts**: log any catalog workout (e.g. *Ab Attack:10*) or a free-text one on any day,
  independent of the active program; shown on the calendar as a sticker/dot, never as the cross.
- Reminders (daily time, per weekday).
- Custom schedule builder.
- Program **hero images**.
- Offline-first; Supabase auth + cross-device sync.
- Free / Pro tiers.
- All six heads: Android, iOS, Windows (WinAppSDK), Desktop Skia (macOS, Linux, Windows), WebAssembly.
- Responsive from a 360 px phone to a 4K desktop.

## Non-goals (v1)

- Video playback or workout instructions — we track, we don't stream.
- Social features, leaderboards, coaches.
- Nutrition, body measurements, weights per exercise (possible Pro later).
- Apple Health / Google Fit integration (later).

## Anti-goals

- **Using Bodi logos, key art, photos or video.** Program names used nominatively only, with a visible
  "Not affiliated with or endorsed by Bodi / Beachbody" disclaimer. Hero art is original or properly
  licensed.
- Shipping an **unverified** calendar. A wrong calendar for a paid program is worse than no calendar.
- Losing a crossed day. Sync must never drop a completion.
- A crossing interaction that feels like a checkbox. If it doesn't feel good, it isn't done.

## Constraints

- Starts from `MartinZikmund/uno-app-template` (plain WinUI/XAML + CommunityToolkit.Mvvm, Core/head split,
  DI, type-driven navigation, localization EN+CS, NBGV versioning, CI + release pipelines). The template's
  `AGENTS.md` / `.claude/rules` conventions apply: **no new Uno.Extensions Navigation/MVUX/C# Markup, prefer
  WinUI/CommunityToolkit over Uno Toolkit/Themes.** The shell may be rebuilt.
- **Uno.Sdk 7.1 latest dev** (currently `7.1.0-dev.36`); template is on 6.7 dev, so the bump is milestone 1.
- Design follows `/winui-design` (Fluent), customized to the app's own theme.
- Martin's C# style (file-scoped namespaces, nullable, target-typed `new()`, braces always, expression bodies).
- Fix and test in separate commits; TDD where possible.

## Approaches considered (data & sync)

| | Approach | Trade-off |
|---|---|---|
| ✅ **Picked** | **SQLite locally** (`sqlite-net-e` + `SourceGear.sqlite3`, already in the template on all heads), mirrored tables in Supabase, row sync by `updated_at` cursor. Completions are **one row per crossed session** (GUID, `deleted_at` tombstone) so merges add rather than overwrite | Familiar, queryable, easy stats. Risk: SQLite on WASM — spiked first; WASM-only fallback to a JSON-backed repository behind the same interface. |
| ✗ | Append-only event log (JSON) + projection | Conflict-free and portable, but Martin wasn't comfortable with it as the store. |
| ✗ | Supabase-first with cache | Simplest, but offline crossing becomes the edge case — against the accountability goal. |

## Decisions

### Product
- **Name:** Show Up. **Store product.** Namespaces `ShowUp`, `ShowUp.Core`; app IDs `dev.zikmund.showup`
  (Prod) / `dev.zikmund.showup.dev` (Dev) — *builder's call on exact IDs, following the template's channel
  scheme.*
- **Free tier:** full catalog browsing, **one active program**, bonus workouts, one daily reminder, local
  tracking + history of the current program.
- **Pro:** cloud sync across devices, custom schedule builder, multiple concurrent programs, multiple
  reminders, full stats/streak history, extra themes and marker styles (colors, stroke styles).
- **Billing:** Android + iOS via Martin's `Uno.RevenueCat` (one entitlement `pro`; monthly, yearly,
  lifetime). **All other heads unlock Pro** for now (`IEntitlementService` returns Pro).
- **Account:** optional. App is fully usable signed-out. Sign-in (Supabase Auth: email magic link first,
  Apple/Google OAuth later) is required only for sync. Signing in can claim existing local progress (see Sync).

### Catalog
- **Workouts are a global catalog** (`catalog/workouts.json`) referenced by sessions, so bonus workouts
  can pick any workout (e.g. *Ab Attack:10*). *(my call)*
- **Model:** `Program` → `Phase[]` (optional, e.g. Month 1, Recovery, Month 2) → `Week[]` → `Day[]` →
  `Session[]`. A `Session` references a `Workout` (id, name, duration, kind: cardio/strength/abs/yoga/
  stretch/rest/fit-test, optional "alternate" workout ids). Rest days are explicit sessions of kind rest.
  Programs may declare **tracks/variants** (e.g. P90X Classic / Lean / Doubles) as separate schedules of
  the same program.
- **Verification rule (Martin: lowered bar):** a program ships if its calendar is confirmed by the
  **official calendar** *or* by **at least two independent sources that agree**. Each program records
  `verification: official | community`, `sources[]` (URL + what it confirmed) and `verifiedOn`.
  Community-verified programs show a small "community-verified calendar — report an issue" note.
  Conflicts → official wins; without an official source, the program is held and logged in an issue.
- **Delivery:** source of truth is JSON files in the repo (`catalog/programs/*.json`), validated in CI
  against a JSON schema and by Core tests (e.g. day counts, all workout refs resolve). A CI job upserts
  them into Supabase `catalog_programs` (versioned, `catalog_version` row). The app **bundles a snapshot**
  of the full catalog at build time (it's small) and fetches newer versions when online — so first launch
  offline works and fixes ship without app releases.
- **Hero images:** original/licensed art per program, stored in Supabase Storage (`program-heroes`
  bucket), referenced by URL in catalog; the bundled snapshot includes small, compressed versions.
  Fallback: generated gradient card with program initials and accent color from catalog.
- **Initial catalog target** (verified only): Insanity, Insanity MAX:30, Insanity: The Asylum Vol. 1,
  Insanity Unhinged (+ its short workouts), Shaun Week, P90X (Classic/Lean/Doubles), P90X2, P90X3,
  21 Day Fix, 21 Day Fix Extreme, T25 (Focus T25), Hip Hop Abs, Core De Force, 10-Rounds, Body Beast,
  LIIFT4, 9 Week Control Freak, Ab Attack:10 and other short standalones as bonus-workout entries.
  Plus a few non-Bodi generic ones (Couch to 5K-style, 30-day plank/push-up challenge). Anything that
  can't be verified is dropped, not guessed.

### Tracking & data model
- Local SQLite tables (all rows: `id uuid`, `user_id` nullable, `created_at`, `updated_at`, `deleted_at`,
  `device_id`): `enrollments(program_id, program_revision, track, start_date, ended_at, end_reason)`,
  `schedule_shifts(enrollment_id, from_date, days)`, `completions(enrollment_id, day_index,
  session_index, completed_on_local_date, completed_at)`, `bonus_workouts(workout_id?, title, date,
  duration_min?)`, `custom_programs(json, revision)`, `reminders(...)`, `synced_settings(key, value)`,
  plus local-only `catalog_cache` and `sync_state`. Migrations versioned in code (`PRAGMA user_version`).
- A session is crossed if a non-deleted completion row exists for (enrollment, day, session); duplicates
  from two devices collapse. Un-cross = set `deleted_at` (with confirmation).
- **Enrollments pin a catalog revision.** The catalog keeps every published revision (small); an
  in-progress enrollment keeps its revision, and the user may opt in to "update to latest calendar" if
  the day/session structure is compatible. *(my call)*
- **Crossing rules (Martin):** today and **any past day** of the enrollment can be crossed; future days
  are locked. "Today" = device local date. A session crossed on a later date is marked *late* in history
  (subtle), same X.
- **Rest days** are crossable (paper calendars do it, it's satisfying) but an uncrossed rest day never
  breaks a streak. Streak = consecutive scheduled training days with all sessions crossed. *(my call)*
- Missed days stay as empty boxes. "Shift schedule" (free — accountability isn't paywalled) pushes the
  remainder by N days.

### Sync
- Supabase mirrors the synced tables (same columns + `server_updated_at` set by trigger), RLS
  `user_id = auth.uid()` on all operations; no hard deletes (tombstones only).
- Device pushes rows with `updated_at > last_push` (upsert by id, LWW on `updated_at` for mutable rows),
  pulls rows with `server_updated_at > cursor`. Runs on launch, on resume, after local changes
  (debounced). Pro-only.
- **Account switching** *(my call)*: rows created signed-out have `user_id = null`. On first sign-in:
  "Bring your local progress into this account?" — yes claims them. Each account's data is kept
  separate locally; signing into a different account never uploads another account's rows.
- **Lapse/downgrade** *(my call)*: nothing is ever deleted. Sync pauses, extra active programs and
  custom programs become read-only (user picks which program stays active), extra reminders are
  disabled but kept.

### UX & shell
- **Sections:** *Today* (what's on today: session cards with big "Cross it off" + bonus workout button),
  *Calendar* (the paper calendar of the active program — the hero screen), *Programs* (catalog browse,
  filters, program detail with hero + calendar preview + "Start"), *History/Stats*, *Settings*
  (account, Pro, reminders, theme, sound/haptics toggles).
- **Responsive:** < 640 px: bottom tab bar (custom, Fluent-styled), single column, calendar as week rows
  that scroll. 640–1007: NavigationView left compact, calendar full month/phase grid. ≥ 1008: NavigationView
  expanded, Today + Calendar side by side. Calendar cells scale; the X stays legible down to ~40 px cells.
- **Theme:** Fluent base (Mica on Windows), customized: warm off-white "paper" surface with subtle
  grid lines in light theme, deep charcoal "chalkboard" in dark; marker-red accent default (user-selectable
  marker colors in Pro); a bold condensed display font for numbers and headers (open-licensed, e.g.
  *Barlow Condensed*), body in the platform Fluent font. Light, dark, high-contrast all supported.
- **The cross:** custom `MarkerCrossControl` — two Bezier strokes generated with slight per-instance
  randomness (seeded by completion id, so the same X looks the same on every device), drawn via animated
  `StrokeDashOffset`, ~180 ms per stroke with ease-out, ink-texture via a slightly wobbling width (or
  layered paths). Sound: short bundled marker-squeak clips (2–3 variants, CC0 or self-recorded), one
  per stroke. Haptics: a short tick per stroke on Android/iOS (and Windows where supported). Respects
  reduced-motion (instant X) and sound/haptic toggles. Small confetti/streak celebration on finishing a
  week/phase/program.
- **Bonus workouts** appear on the calendar day as a small round sticker with a count; tap to see/edit.

### Reminders
- Per-platform local notifications behind `IReminderScheduler`: Android (AlarmManager + notification
  channel, exact alarm permission handling), iOS (UNUserNotificationCenter), Windows WinAppSDK
  (AppNotificationManager + scheduled toasts), WASM (Web Notifications — only while a tab is open;
  documented), Desktop Skia (in-app reminder while running; documented limitation). Reminder text
  names the day's workout ("Plyometric Cardio Circuit is waiting. Show up.").

## Engineering & process

- Public repo `MartinZikmund/show-up`, created from the template's content (not a fork), MIT like the
  template. README with disclaimer and build-in-public log.
- Work in milestones; each milestone = GitHub issue(s), branch, PR that closes them, CI green, merged by
  Claude (squash). Commits pushed as work progresses.
- Tests: MSTest in `tests/ShowUp.Core.Tests` for catalog validation, schedule engine, repositories
  (in-memory SQLite), sync merge, entitlements. UI verified via the Uno-app MCP (Desktop/WASM) and winapp (WinUI) with screenshots.
- Supabase: `supabase/` folder with migrations, RLS policies, storage bucket config, seed; the app reads
  URL + anon key from config. **No live project yet** — Martin plugs in credentials later; the app runs
  fully without them.

## Milestones (order)

1. Bootstrap: repo from template, rename to ShowUp, bump to Uno.Sdk 7.1 dev, CI green on all heads.
2. Core domain: catalog model + JSON schema, schedule engine, SQLite storage + migrations, completion/streak logic (TDD); WASM SQLite spike first.
3. Catalog research & verification (parallel subagents per program family) → `catalog/programs/*.json`.
4. Shell & theme: responsive nav, theme resources, fonts, page skeletons.
5. Calendar + `MarkerCrossControl` (animation, sound, haptics) + Today page.
6. Programs browse/detail with hero images; enroll flow; bonus workouts.
7. Entitlements (Free/Pro gates, RevenueCat on mobile).
8. Reminders per platform.
9. Custom schedule builder (Pro).
10. Supabase schema + auth + sync; catalog publish job.
11. Stats/history, celebrations, polish, accessibility pass, store metadata.

## Builder-check calls (mine, for review)

- **Credentials** (Apple, Google Play, RevenueCat, Supabase, signing): none assumed; behind config/secrets;
  CI builds unsigned packages until Martin supplies them.
- **Uno 7.1 dev breakage:** stay on 7.1 dev, work around in-app, record findings in `~/.claude/uno-issues`;
  fall back to an older dev build only if a head is fully blocked.
- Plus the *(my call)* items above: revision pinning, rest days, account switching, lapse behaviour,
  global workouts catalog.

## Left to the builder

- Exact package choices (Supabase C# client vs. thin REST client over `HttpClient` — prefer the thinner
  option if AOT/trimming on WASM is a problem), audio playback API per head, haptics API per head.
- Exact TFMs required by Uno 7.1 dev.
- Visual details within the theme direction above.
- Naming of types, file layout within the Core/head split.
