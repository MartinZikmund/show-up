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
while it's active, else the newest. The selection is per device (`IAppPreferences.SelectedEnrollmentId`,
not synced), so a phone and a laptop can each show a different program. With more than one program
running, a program `ComboBox` (`ProgramOption`) replaces the Calendar title; Results has one too.
Starting a program selects it. A program's page opens Results for that program
(`Navigate<ProgramResultsViewModel>(enrollmentId)`) without changing the selection.

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

- `LapseCoordinator` (app-wide singleton; each `WindowShell` attaches its `IPrimaryProgramPrompt`)
  asks once which program stays primary. It waits for the first entitlement refresh after launch, so a
  subscription that renewed while the app was closed never prompts. Dismissing keeps the most recently
  started.
  The choice is the synced setting `primary-enrollment`; while it names an active enrollment the
  prompt doesn't come back.
- `IsReadOnly(enrollment)` is true for the others. Crossing, uncrossing, shifting and journal writes
  throw `EnrollmentReadOnlyException`; Calendar and Today show a banner with "Unlock Pro to run
  several programs", the day sheet and program note open without Save.
- Getting Pro back clears read-only at once: the service re-sends `TrackingChangedMessage` on every
  `IEntitlementService.Changed`.
- At a cached subscription's end mid-session, the Store and RevenueCat services ask the store again
  (`ExpiryWatch`) and only lock when the answer is still "not Pro" or the store can't be reached.

Replacing the primary program makes the new one primary. A read-only program can't be restarted
(Program detail explains why and shows the Pro link).

## Trying it out

Desktop and WebAssembly builds are always Pro. Debug builds wrap the entitlement service in
`DebugEntitlementOverride`; **Settings > Debugging > Simulate Free** flips it to Free to walk
through the lapse flow.
