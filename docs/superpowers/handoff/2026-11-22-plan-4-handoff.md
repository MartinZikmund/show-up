# Show Up — paused 2026-11-22 (handoff)

## Where things stand
- Plans 1–3 done and merged. Plan 4 (Supabase) Tasks 1–6 all merged on `main` (PRs #76–#81; #17 and #18 closed).
- Plan 4 **final whole-branch review** done (Opus): no Criticals, "Needs fix wave".
- **Fix wave DONE** on branch `fix/plan4-final-wave` (from main `1bfb11a`, 18 fix commits + this handoff, pushed, no PR yet; full report `plan4-final-fix-report.md`). 1167 tests pass,
  all five heads + tools/publish-catalog build. Not yet re-reviewed; the new pgTAP `column_size_limits_test.sql` has never run (CI's
  database job is its first run). Fixer notes: revocation during an interactive sign-in doesn't set the session-ended flag;
  empty-server first publish is now version 2; schedule builder gained a localized "too big to save" error.

## Pick up here
1. Fix wave is complete — start at step 2.
2. Package the fix range: `bash <sdd>/scripts/review-package docs/superpowers/plans/2026-11-21-plan-4-supabase.md 1bfb11a HEAD`
   and run ONE scoped re-review (re-review-prompt.md). Rule on leftovers (no second fix wave).
3. Push `fix/plan4-final-wave`, PR (Refs #17 #18), merge when green (CI `database` job runs the new pgTAP size checks).
4. Close Plan 4: delete this workspace folder **after** copying the rulings out (see below), flip nothing else — Cloud sync
   already shows as available exactly when `Supabase:Url`/`PublishableKey` are set.
5. Wrap-up owed to Martin: "Rulings I made" (all four plans), Uno issues list, offer to file the RevenueCat
   Fragment.Ktx dependency issue on `MZikmund/Uno.RevenueCat`, worktree cleanup (only `D:/Personal/show-up` itself exists now).

## Fix-wave findings (complete list)
Important: (1) publisher partial-publish recovery — read row `version`s, recover orphan N+1 by inserting the
`catalog_versions` row; (2) `docs/supabase.md` — `{{ .Token }}` in BOTH Magic Link and Confirm signup templates;
(3) `AccountService` — SetStoreUserAsync any exception must not skip Announce/Changed (try/finally).
Minor: Paused not overwritten by Idle at pass end; first publish ≥ bundled version + 1; clear `_pinned` in
`CatalogService.Invalidate`; "session ended — sign in again" flag on the account card; server column size CHECKs
(ink_json 64 KB, program_json 256 KB, notes 16 KB, text_value 4 KB, with matching client validation); stale docs/comments
(entitlements, accounts, sync-classification, storage v5/v6 rows, supabase.md T4 hand-off section, csproj Credential Locker
comment, README label); pin test-job checkout SHA in catalog-publish.yml.

## Deferred (decided, not bugs to fix now)
Parked minors triaged DEFER by the final review: EndInteractiveAsync masking, double announce, log real errors after switch
at Warning, pull even when push fails, catch-all counts local bugs, push-switch test exact exception, DEBUG ProViewModel
staleness, GetAllAsync page cap. Server-side Pro enforcement (RevenueCat webhooks) — a later plan.

## What's next after Plan 4
- **Martin's inputs to go live:** Supabase project (URL + publishable key; follow `docs/supabase.md` "Going live"),
  custom SMTP, GitHub secrets `SUPABASE_URL`/`SUPABASE_SECRET_KEY` + `catalog` environment restricted to main,
  RevenueCat keys, Microsoft Store add-on ids.
- **Plan 5** (not written yet): hero art (#9), history/stats (#22), celebrations + accessibility pass + store metadata (#19),
  release. Also open: #68 family-picker InvalidCastException, #58 merge Today into Calendar, #34 verify SQLite on
  Release WASM + packaged Windows, catalog follow-ups #36–#45, #21 Achievements, #23 Community.
  Check whether #16 (custom builder) can be closed — it shipped in Plan 3.

## Rulings
Plan 1–3 rulings: `plan1-rulings.md`, `plan2-rulings.md`, `plan3-rulings.md` (copied next to this file). Plan 4: `plan4-rulings.md` (all `Ruling:` lines from the ledger;
the full ledger is in the git-ignored `.superpowers/sdd/2026-11-21-plan-4-supabase/progress.md`). All of them go into the wrap-up message, each with its cost-if-wrong.
