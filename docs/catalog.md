# Program catalog

How the bundled workout programs are checked, what the files look like, and how to fix a mistake.

## Verification levels

- **official**: every day matches a calendar the program publisher released (PDF, coaching guide, NHS page).
- **community**: no usable official calendar exists, so at least two independent public pages agree on every day.

Each program file lists its sources and the date it was verified. The per-program table, with fixes the verifier made, is in [`catalog/VERIFICATION.md`](../catalog/VERIFICATION.md). When sources conflict, we hold the program rather than guess.

## Product choices

These are ours, not the source's:

- **7-Minute Workout** repeats the same circuit for 30 days.
- **10 Rounds** has two rest days after each five-workout week; the official calendar only says "rest two days a week".
- **Couch to 5K** is our weekday layout of the NHS plan: Run, Rest, Run, Rest, Run, Rest, Rest.
- **P90X2** uses the official minimum phase lengths.

## File format

- `catalog/workouts.json`: every workout (`id`, `name`, `durationMinutes`, `kind`, `family`, optional `metrics`), described by [`catalog/schema/workouts.schema.json`](../catalog/schema/workouts.schema.json).
- **Metrics** are the numbers a program asks you to write down: `{ "id": "power-jacks", "name": "Power Jacks", "unit": "reps" }`. Units: `reps`, `count`, `seconds`, `duration` (typed `mm:ss`, stored as seconds), `weight`, `distance`, `text`. `higherIsBetter` (default `true`) colours the deltas in the results table. Ids must be unique within a workout. Only add metrics an official worksheet asks for, with the source in `catalog/VERIFICATION.md`.
- `catalog/programs/<id>.json`: one program, validated by [`catalog/schema/program.schema.json`](../catalog/schema/program.schema.json) and by `CatalogValidator` in `ShowUp.Core.Catalog`.
- A program has tracks, tracks have phases, phases have weeks, weeks have days, days have sessions. A session is `{ "workoutId": "...", "alternates": [...], "optional": true }`; `alternates` and `optional` can be left out.
- **Optional sessions** (for example T25's second Friday workout) can be crossed off but never count toward completing the day, streaks or missed days. A day with only optional sessions behaves like a rest day.

- **Workout ids are append-only.** Custom programs and enrollments pin them. `catalog/workout-ids.lock` lists every id, sorted; `CatalogFolderTests` fails if one disappears from `workouts.json`, and also if a new one isn't appended to the lock. If a program is saved with an id that later vanishes anyway, it still loads and shows an "Unknown workout" placeholder (logged as a warning).

`CatalogFolderTests.CatalogFolder_AllFiles_AreValid` parses and validates the whole folder on every test run.

## Proposing a correction

Open an issue with the [bug report template](https://github.com/MartinZikmund/show-up/issues/new?template=bug-report.yml) and link the source that shows the right day. Official calendars win over community pages.

## Programs

Official: Insanity, Insanity MAX:30, Insanity Asylum Vol. 1 and 2, Insanity Unhinged (100 and 50 day), P90X2, P90X3 (Classic, Lean, Mass, Doubles, Elite), 21 Day Fix (Standard, Doubles), 21 Day Fix Extreme, Body Beast (Huge, Lean), Focus T25 (Alpha + Beta), LIIFT4, 9 Week Control Freak, Hip Hop Abs, Core De Force (Original), 10 Rounds, Couch to 5K.

Community: Shaun Week, P90X (Classic), 30-Day Squat Challenge, 7-Minute Workout (30 days).

## Held programs

Tracked as issues labelled `catalog`:

- P90X Lean: sources disagree on Day 5 of the recovery weeks and the official PDF is blank.
- P90X Doubles and P90X3 Elite Doubles: no official week-by-week source.
- Core De Force Hybrid and Deluxe.
- T25 Gamma: no official calendar found.
- 80 Day Obsession.
- Insanity MAX:30 Ab Maximizer variant.
- 30-day plank and push-up challenges: sources disagree.
- Hip Hop Abs 30-day calendar.
- 21 Day Fix Real Time.
