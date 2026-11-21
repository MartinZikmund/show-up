# Multiple programs

Pro users can run several programs at once, say INSANITY: Unhinged and Couch to 5K. Free runs one.
This page covers how the app addresses each program and what happens when Pro lapses.

## Every write names its enrollment

`IActiveEnrollmentService`, `ICrossingCoordinator` and `IJournalService` take an explicit
`Guid enrollmentId`; there is no "the active enrollment" anymore.

```csharp
using ShowUp.Core.Tracking;

IReadOnlyList<ActiveEnrollment> all = await enrollments.GetAllAsync(); // newest first
await enrollments.CrossAsync(enrollmentId, dayIndex, sessionIndex);
await journal.SaveProgramNoteAsync(enrollmentId, "Round two");
```

`CrossTarget` carries `EnrollmentId`, so the cross moment's "Log your results" opens that
program's sheet. `IJournalDialogs` methods take the id too.

## The selected program

Calendar and Results show one program at a time: `GetSelectedAsync()` returns the stored selection
while it's active, else the newest. `SelectAsync(id)` writes the synced setting
`selected-enrollment` (newest write wins across devices). Both pages show a program `ComboBox`
(`ProgramOption`) when more than one program runs. Starting a program selects it.

## Today

`IScheduleQuery.GetDueAsync(date)` returns a `DueProgram` per active enrollment (selected first):
its day statuses, completions, the date's `DayIndex` (null when nothing is scheduled) and whether
it's read-only. Today renders one `TodayProgramViewModel` group per entry, each with its own sessions,
marks and streak. Reminders read the same query.

## Starting a program

See [programs.md](./programs.md). Pro starts alongside; Free (or restarting a running program)
replaces. `EnrollAsync` itself refuses a second program for Free.

## When Pro lapses

Nothing is deleted. With more programs running than Free allows:

- `LapseCoordinator` (per window, started from `WindowShell`) asks once which program stays
  primary (`IPrimaryProgramPrompt`, a `ContentDialog`). Dismissing keeps the most recently started.
  The choice is the synced setting `primary-enrollment`; while it names an active enrollment the
  prompt doesn't come back.
- `IsReadOnly(enrollment)` is true for the others. Crossing, uncrossing, shifting and journal writes
  throw `EnrollmentReadOnlyException`; Calendar and Today show a banner with "Unlock Pro to run
  several programs", the day sheet and program note open without Save.
- Getting Pro back clears read-only at once: the service re-sends `TrackingChangedMessage` on every
  `IEntitlementService.Changed`.

Replacing the primary program makes the new one primary.

## Trying it out

Desktop and WebAssembly builds are always Pro. Debug builds wrap the entitlement service in
`DebugEntitlementOverride`; **Settings > Debugging > Simulate Free** flips it to Free to walk
through the lapse flow.
