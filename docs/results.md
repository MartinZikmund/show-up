# Notes and results

Some programs ask you to write numbers down: the Insanity Fit Test (reps for 8 moves), the MAX:30 Max Out time, the Asylum tests, the Unhinged Fit Test. Show Up keeps those numbers, plus free-text notes, and lines every test up side by side.

## Where it shows up

- **Log your results**: crossing off a workout that has metrics adds a *Log your results* button to the cross moment. The moment still dismisses on its own (after 5 s instead of 2.5 s); the button closes it and opens the results dialog.
- **Day detail**: tapping a crossed day on the calendar opens the day's sheet (full height on phones, a 480 px dialog when wider). It lists each session with its crossed state, a note and its metric inputs, the day's bonus workouts with theirs, and a day note. Uncross lives here now: it marks the session ("Uncrossed when you save", with Undo) and is applied on Save. The cell's context menu also has *Day details*.
- **Bonus workouts**: logging a bonus workout that has metrics (for example the Unhinged Fit Test, which the calendar never schedules) opens its results dialog straight after.
- **Results page**: from the calendar header (*Results*) and from the active program's detail page. One table per workout with logged values: rows are metrics (with their unit), columns are the occurrences up to today plus the next one, ordered by day, plus bonus entries with values (from a week before Day 1). Workouts with nothing logged yet collapse into one line: *Not logged yet · next: Day N*. Each cell reads to screen readers as metric, column, value and change. Each cell shows the change against the row's first value, green when better and red when worse (`higherIsBetter` on the metric decides). The metric column stays pinned while the cells scroll sideways.
- **Program notes**: a button in the calendar header, the results page and program detail.

Everything is saved only on an explicit Save; Cancel discards the edits.

## Inputs

| Unit | Input | Stored as |
|---|---|---|
| `reps`, `count`, `seconds` | NumberBox, whole numbers | the number |
| `weight`, `distance` | NumberBox, two decimals, in the Settings unit system (Metric or Imperial, seeded from the region once) | kg / km at full precision |
| `duration` | TextBox, `mm:ss` (`DurationParser`) | seconds |
| `text` | TextBox | `TextValue` |

## Code

- `ShowUp.Core.Catalog.WorkoutMetric`: catalog definitions, see [catalog.md](./catalog.md#file-format).
- `IJournalService` (`JournalService`): the only path that writes notes and results. Every call names its enrollment (`Guid enrollmentId`); notes and session results need it active and writable, bonus results don't.
- `INotesRepository` / `IMetricsRepository`: sqlite, upsert by natural key, tombstones, scoped to the current user, each save in one transaction. A note's key is its `SubjectKey` (`p:{enrollment}`, `d:{enrollment}:{day}`, `s:{enrollment}:{day}:{session}`). A result's key is `(SubjectKey, WorkoutId, MetricId)`, where `SubjectKey` is `s:{enrollment}:{day}:{session}` or `b:{bonus}`: the same session can hold results for a different workout after a catalog change, so `SubjectKey` alone isn't enough.
- **Sync: no plain unique constraint** (same rule for every keyed table, see [storage.md](./storage.md#server-merge-for-keyed-rows)). Two devices can create a row for the same key while offline, so the server must not reject the second one. It either merges on the key, last writer wins (`UpdatedAt`, then `Id`), or keeps a non-unique index on it. The local reads already do this: when two live rows share a key, only the newest is read, and the next save tombstones the rest.
- `MetricUnits`: kg/km to lb/mi and back; `MetricFormat` shows values and changes in the user's units.
- `ResultsTable.Build`: the pure table builder.
- `DayDetailViewModel`, `LogResultsViewModel`, `ProgramResultsViewModel`: the view models; `IJournalDialogs` opens the dialogs.

Free: notes, logging results, the table. Charts and trends come later with statistics.
