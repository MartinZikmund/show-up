# Plan 5 rulings

Every decision made on Martin's behalf while Plan 5 ran, in the order it was made, each with what it costs if wrong.
The plan is `docs/superpowers/plans/2026-11-23-plan-5-finish-line.md`.

- finishing a program is offered, never automatic — a mis-tapped last cross stays undoable and sync never ends a program — cost if wrong: rework Task 1/2 to auto-finish.
- Task 1 finish tests live in ActiveEnrollmentServiceTests (shared fixture) instead of a new FinishProgramTests.cs — cost if wrong: none.
- coordinator reads the track via IActiveEnrollmentService.GetAsync instead of widening CrossTarget — cost if wrong: one extra cached read per cross.
- statistics count a crossed session on its calendar day (Calendar.DateOf), not CompletedOn as the plan said — backfilling five days on Sunday must not show as five workouts on Sunday — cost if wrong: one line in StatsService.SessionEntries.
- Tasks 3 and 4 share one branch/PR (feature/22-statistics, Closes #22) — the view model can't be registered without its view — cost if wrong: none.
- statistics rank and scale by workouts, not minutes — 180 of 270 catalog workouts have no duration (#88) — time shown only where known, time tile hidden at 0 — cost if wrong: re-rank once durations exist.
- the moment's "Finish program" only finishes (the coordinator has no navigator); the Calendar banner's finish opens History — cost if wrong: add a navigate hook to CrossHooks.
- milestone headline uses MarkerBrush + SectionHeaderTextBlockStyle; no new sound asset, one extra SessionTicked — cost if wrong: restyle.
- for a finished program the caption's progress line is "You showed up. All the way through." (never "Show up again tomorrow") — cost if wrong: copy change.
- CalendarFocusMap works on (week, day) with per-week lengths instead of the plan's flat index — weeks are separate panels and can be short — cost if wrong: none.
- accessibility audit found InkPad and dialogs already named, contrast already >= 4.5:1 (5.04-6.14) — no change made there — cost if wrong: none.
- icon = concept B (calendar page crossed off, red header) — A reads as error/close, C as delete — cost if wrong: swap SVGs; PR #93 deliberately LEFT UNMERGED for Martin (taste call).
- hero art is JPEG not WebP (WinUI needs a Store codec for WebP; a failed bundled decode leaves a blank hero) — cost if wrong: re-encode.
- heroImage added to revision 1 in place (no hosted project was ever published to) — cost if wrong: bump 21 revisions before the first publish.
- store listing guard is an MSTest test (Packaging/StoreListingTests) instead of the plan's Python script + CI step — matches the repo's other repo-file checks — cost if wrong: none.
- listing leaves out cloud sync (not live) and says 17 official / 4 community honestly; one sentence names five programs (nominative use, spec allows) — cost if wrong: store rejection of that sentence; shorten it.
- store screenshots NOT captured in this plan — filed as #96; in-app account deletion found missing — filed as #95 (store blocker) — cost if wrong: none, both are tracked.
- M5 (Free sees "Workouts by kind") kept — it only covers the current program's last 28 days, which Free already sees as bars — cost if wrong: gate one section.
- M8 — a complete program that went read-only (lapsed Pro, non-primary) CAN be finished, like EndAsync already allows stopping it; "finishing is Free" is a Global Constraint — cost if wrong: restore RequireWritableAsync in FinishAsync.
- Tab-lands-on-today when today's week is scrolled away is implemented but unverified (GetOrCreateElement may hand back an unarranged element; then Tab lands on the default cell as before) — parked — cost if wrong: one extra arrow-key trip.
- M2(b) left as is — when the X is drawn, the celebration shows before the cross is saved, and a failed save leaves it up with no message — pre-existing draw-flow behaviour, rare (the enrollment has to end or go read-only mid-drawing) — cost if wrong: a celebration for a cross that did not save.
- TextLimit.Truncate returns an empty string when a single text element is longer than the limit — unreachable in practice (limits are 200 and 4000) — cost if wrong: none.
- three copies of the Lazy<AccessibilitySettings> high-contrast check (CalendarDayCell, ProgramHero, CrossMomentOverlay) left unconsolidated — cost if wrong: a fourth copy; fold into IMotionPreference later.
- merged every Plan 5 PR myself once CI was green, with merge commits, except #93 (app icon), which is a taste call and stays open for Martin — cost if wrong: revert a merge.
