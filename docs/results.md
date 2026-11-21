# Notes and results

Some programs ask you to write numbers down: the Insanity Fit Test (reps for 8 moves), the MAX:30 Max Out time, the Asylum tests, the Unhinged Fit Test. Show Up keeps those numbers, plus free-text notes, and lines every test up side by side.

## Where it shows up

- **Log your results**: crossing off a workout that has metrics adds a *Log your results* button to the cross moment. The moment still dismisses on its own (after 5 s instead of 2.5 s); the button closes it and opens the results dialog.
- **Day detail**: tapping a crossed day on the calendar opens the day's sheet (full height on phones, a 480 px dialog when wider). It lists each session with its crossed state, a note and its metric inputs, the day's bonus workouts with theirs, and a day note. Uncross lives here now, behind a confirm flyout. The cell's context menu also has *Day details*.
- **Bonus workouts**: logging a bonus workout that has metrics (for example the Unhinged Fit Test, which the calendar never schedules) opens its results dialog straight after.
- **Results page**: from the calendar header (*Results*) and from the active program's detail page. One table per workout with metrics: rows are metrics, columns are every scheduled occurrence ordered by day, plus bonus entries with values. Each cell shows the change against the row's first value, green when better and red when worse (`higherIsBetter` on the metric decides). The metric column stays pinned while the cells scroll sideways.
- **Program notes**: a button in the calendar header, the results page and program detail.

Everything is saved only on an explicit Save; Cancel discards the edits.

## Inputs

| Unit | Input | Stored as |
|---|---|---|
| `reps`, `count`, `seconds` | NumberBox, whole numbers | the number |
| `weight`, `distance` | NumberBox, two decimals, `kg`/`lb` or `km`/`mi` by region | the number, as typed |
| `duration` | TextBox, `mm:ss` (`DurationParser`) | seconds |
| `text` | TextBox | `TextValue` |

## Code

- `ShowUp.Core.Catalog.WorkoutMetric`: catalog definitions, see [catalog.md](./catalog.md#file-format).
- `IJournalService` (`JournalService`): the only path that writes notes and results. Notes need the active enrollment; bonus results don't.
- `INotesRepository` / `IMetricsRepository`: sqlite, upsert by natural key, tombstones, scoped to the current user. A session result is keyed by enrollment, day, session and metric; a bonus result by bonus workout id and metric.
- `ResultsTable.Build`: the pure table builder.
- `DayDetailViewModel`, `LogResultsViewModel`, `ProgramResultsViewModel`: the view models; `IJournalDialogs` opens the dialogs.

Free: notes, logging results, the table. Charts and trends come later with statistics.
