# Custom schedules

Pro users build their own programs on the Programs page ("My schedules" → **New schedule**). A
custom program behaves like a catalog one everywhere: start it, see it on Calendar and Today,
cross days off, and log results when a session's workout has metrics.

## The builder

`ScheduleBuilderViewModel` (Core) edits a `CustomProgramDraft`: a name (up to 60 characters), one
of eight accent colors, 1–26 weeks of seven days, and per day a list of sessions. Each day can hold:

- a **catalog workout**, picked in the session picker (`ISessionPickerDialog`), which reuses the
  bonus dialog's name search (`WorkoutSearch`);
- a **workout of your own**: type a title that isn't in the list, then pick a kind and minutes;
- **rest** (a day with the `rest` workout and nothing else).

Sessions can be reordered with the up/down buttons and marked optional (optional sessions never
count toward finishing a day). A week can be copied to the next week or to all weeks. Lowering the
week count hides the extra weeks without losing them, until the page is left.

Save stays disabled until the schedule is valid: a name, 1–26 weeks, and no empty day. Problems
show inline (name, weeks, a hint on each empty day) and as a summary above Save.

Phones get one column with collapsible weeks; at 1008 px and wider the week list sits on the
left and the selected week's days on the right.

## Storage and revisions

`ICustomProgramService.SaveAsync` turns the draft into a `WorkoutProgram` with
`Verification = Custom` (`CustomProgramFactory`) and stores its JSON through
`ICustomProgramRepository.SaveNewRevisionAsync` (see [storage.md](./storage.md)). Ids are
`custom-<guid>`; the user's own workouts live inside the program JSON in a `workouts` array, with
ids `custom-<guid>-<n>`. Numbers are never reused, and every workout a program ever had stays in
the array, so older revisions keep resolving their sessions.

Every save is a new revision; enrollments stay pinned to the revision they started with. Stored
revision numbers are timestamps, so the UI shows them as `v1…vN` by order
(`GetVersionNumberAsync`).

Delete hides the program with a synced marker; its revisions stay, so a running enrollment keeps
its days and history until it is ended.

## The catalog merge

`CatalogService` reads the bundled catalog once and merges the latest live revision of each custom
program on top, re-reading them after `InvalidateCustomPrograms()` (called on save and delete). A
pinned revision that isn't the latest (or belongs to a deleted program) is fetched on demand by
`GetProgramAsync(id, revision)`, and its workouts join `WorkoutsById`.

`CatalogValidator` accepts the `workouts` array only on custom programs, and only with ids
prefixed by the program id, so a custom workout can't shadow a catalog one. A bundled program
marked `custom` is rejected.

## Without Pro

Custom programs stay listed, but are read-only: no new schedules, no edits, no deletes and no
starting them (`CustomProgramLockedException`, enforced in `CustomProgramService` and
`ActiveEnrollmentService`). A program that is already running follows the usual lapse rules in
[multiple-programs.md](./multiple-programs.md).
