# Programs: browse, detail and start

The Programs page lists the verified catalog; opening a card shows the program, and Start puts it
on the Calendar.

## Browsing

`ProgramsViewModel` (`src/ShowUp.Core/ViewModels`) turns `ICatalogService.GetCatalogAsync()` into
`ProgramCardViewModel`s. `Filter` matches the name case-insensitively; `SelectedFamily` narrows by
family (`FamilyOptions` adds the leading "All families" entry for the picker). `TotalDays` is the
longest track. Cards sit in an `ItemsRepeater` with a `UniformGridLayout` (`MinItemWidth` 280, so
phones get one column).

Day and track counts are pluralized through `PluralForms`: resource keys end in `_One`, `_Few` or
`_Other` (`ProgramDays_Few` is "2 dny" in Czech).

```csharp
using ShowUp.Core.Localization;

string days = localizer.Format("ProgramDays", 3); // looks up ProgramDays_One / _Few / _Other
```

## Detail and start

`ProgramDetailViewModel.OnNavigatedTo(programId)` loads the latest revision. The first track is
selected; a `SelectorBar` appears when there is more than one. The week preview shows each day's
workouts joined with " + ", optional sessions marked "(optional)". The start date defaults to today
and can go back 30 days, so a program already under way can be caught up.

`StartAsync` asks "End {current} and start {new}?" when an enrollment is active, ends it as
`Abandoned`, calls `IActiveEnrollmentService.EnrollAsync`, then switches to the Calendar section.
Declining changes nothing.

Community-verified programs show a note and a "Report an issue" link that opens a prefilled GitHub
issue ("Calendar issue: <program>"). See [catalog.md](./catalog.md) for the verification levels.

## ProgramHero

`ProgramHero` (`src/ShowUp/Controls`) draws `HeroImage` when set. Until the art arrives it falls
back to a diagonal gradient from `AccentColor` (darker to lighter) with the name in Barlow
Condensed, white over a soft shadow and a bottom scrim for contrast.

## Back

The detail page has its own "Back to programs" button: the shell's back button isn't shown on the
non-Windows heads.
