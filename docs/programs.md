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

`StartAsync` starts the program alongside the running ones for Pro. Free with a program running
(or anyone restarting a program that's already running) gets "End {current} and start {new}?" and
`IActiveEnrollmentService.ReplaceAsync` ends the old one as `Abandoned`; Free also sees an "Unlock Pro
to run several programs" link under Start. Either way the new program is selected and the page
switches to the Calendar section.
Declining changes nothing. The start date is clamped to 30 days back and a year ahead, and a failed
end or enroll logs and shows an error dialog instead of crashing. An unknown program id shows a
not-found message with Start disabled.

Community-verified programs show a note and a "Report an issue" link that opens a prefilled GitHub
issue ("Calendar issue: <program>"). See [catalog.md](./catalog.md) for the verification levels.

Cards carry their item in `Tag` and read it in `Card_Click`, not from `DataContext`: Uno sets the
`DataContext` of `ItemsRepeater` items but WinUI does not, so clicks silently did nothing on the
Windows head (see the uno-issues finding `uno--itemsrepeater-sets-datacontext-on-items-unlike-winui`).

`PluralForms` reads `CultureInfo.CurrentUICulture`, the same culture the localizer resolves strings
with, so the plural form and the string always come from one language.

## ProgramHero

`ProgramHero` (`src/ShowUp/Controls`) draws `HeroImage` when set. Until the art arrives it falls
back to a diagonal gradient from `AccentColor` (darker to lighter) with the name in Barlow
Condensed, white over a soft shadow and a bottom scrim for contrast. In high contrast the gradient
is dropped for the system window and text colours with an outline.

## Back

The detail page has its own "Back to programs" button: the shell's back button isn't shown on the
non-Windows heads.
