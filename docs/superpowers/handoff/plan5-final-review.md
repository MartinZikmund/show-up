# Plan 5 — final whole-branch review (e8ebfdb..862fc86)

Reviewer: Opus, read-only. Read: the plan, AGENTS.md + `.claude/rules/*`, the ledger (`progress.md`), the full review
diff, and the call paths behind it (`ActiveEnrollmentService`, `SqliteTrackingRepository`, `LiveRefresher`,
`TodayView`, `ViewBase`, `ProgramTrack.Locate`, `DayStatus`, `ProgramHero`, `docs/windows-packaging.md`).
Nothing was built, run or modified.

## Verdict

**Needs fix wave.** Two Important findings, both small fixes: the Calendar banner's *Finish program* button is dead
when the calendar is embedded in a wide Today page, and the Statistics page is invisible to screen readers on
Windows. Nothing Critical: no data loss, no way to finish an incomplete program, no stuck overlay found.

## Critical

None.

## Important

### I1. The Calendar banner's *Finish program* button does nothing on the wide Today page (WinUI)

- **Where:** `src/ShowUp/Controls/ProgramCalendarControl.xaml:144`
  `Command="{x:Bind ViewModel.FinishCommand, Mode=OneTime}"`.
- **Scenario:** Windows head, window wide enough that Today shows the calendar beside it, a program with every
  required session crossed. The `CalendarFinishBanner` InfoBar opens (`IsOpen` is OneWay and does update), the user
  clicks *Finish program*: nothing happens. The program can still be finished from the Calendar tab or from the
  moment, but the banner a user sees first on desktop is dead.
- **Why I'm confident:** `TodayView.xaml:25-29` declares `CalendarHost` with `ViewModel="{x:Bind CalendarViewModel,
  Mode=OneWay}"`, and `TodayView.xaml.cs:88-91` creates `CalendarViewModel` only in `OnLoaded` →
  `UpdateLayoutState`, then calls `Bindings.Update()`. By then `ProgramCalendarControl` has already run its own
  x:Bind initialization (its `Loading`) with `ViewModel == null`. WinUI's generated x:Bind code re-applies only
  OneWay bindings when a path segment raises a change (`DATA_CHANGED` phase); OneTime bindings are applied only at
  initialization. So `FinishCommand` stays null. Every other command in this control is `Mode=OneWay`
  (`OpenResultsCommand`, `EditProgramNoteCommand`, `ShiftScheduleCommand`, `OpenProCommand`,
  `BrowseProgramsCommand`, lines 83/93/104/129/204) — exactly because of this. The ledger's verification (Task 2)
  was on the Calendar *page*, where `ViewBase` sets the page VM before the control loads, so it worked there.
- **Fix:** `Mode=OneWay` on that binding. A Today-wide check in the next live verification.

### I2. Statistics bars, heatmap cells and program rows are not in the UI Automation tree (Windows)

- **Where:** `src/ShowUp/Views/StatsView.xaml` — `BarTemplate` root `Grid` (≈ line 13), `HeatCellTemplate` root
  `Grid` (≈ line 57), `ProgramTemplate` root `StackPanel` (≈ line 80). Each carries the `AutomationProperties.Name`,
  and every child that could be read is `AutomationProperties.AccessibilityView="Raw"`.
- **Scenario:** Narrator (or any UIA client) on the Statistics page finds the headings and the three totals, and
  nothing for the week bars, the kind bars, the 182 heatmap squares or the program comparison. The plan's Task 3
  requirement ("Every bar and heatmap cell has an automation name … never only as a colour") and the claim in
  `docs/accessibility.md` ("every bar and every heatmap square has a name with the date and the count") do not hold
  on Windows.
- **Why I'm confident:** WinUI's own docs (Custom automation peers → Built-in automation peer classes): "`Border` and
  `Panel`-based layout types such as `Grid` and `Canvas` do not [have peers] … its meaningful child elements are
  surfaced through the nearest ancestor that has a peer." A name set on a peerless Grid/StackPanel is never exposed,
  and the children that would be surfaced are all Raw. The ledger already observed the same thing during Task 4
  verification ("AutomationIds on ItemsRepeater/Border are invisible to UIA"), and Narrator on Statistics is listed
  as not checked.
- **Fix (smallest):** put the name on an element that has a peer. For bars and program rows: drop `Raw` from the
  label `TextBlock` and give it `AutomationProperties.Name="{x:Bind AutomationName}"` (or the value TextBlock), keep
  the others Raw. For heatmap cells: a tiny `FrameworkElement` subclass whose `OnCreateAutomationPeer` returns a
  `FrameworkElementAutomationPeer`, or an empty `TextBlock` in the cell carrying the name. Confirm with
  `winapp ui inspect` (looking for "Week of …" names) and fix the claim in `docs/accessibility.md` if anything stays
  out of reach.

## Minor

1. **Confetti is not collapsed in high contrast.** Plan Task 2: "Collapsed in high contrast." `ConfettiBurst.Play`
   and `CrossMomentOverlay.RevealCaption` (`CrossMomentOverlay.xaml.cs:264-271`) check only `AnimationsEnabled`. No
   ruling covers it. Use the same `AccessibilitySettings.HighContrast` check `CalendarDayCell`/`ProgramHero` use.
2. **Draw-it-yourself shows the celebration and the Finish offer before the cross is written.** `FinishDrawn`
   resolves `_drawn` then immediately calls `RevealCaption` (`CrossMomentOverlay.xaml.cs:386-395`); the coordinator
   writes on its continuation. (a) A very fast *Finish program* can reach `FinishAsync` before the write; it then
   returns `false` and the overlay drops the result silently (`FinishProgramButton_Click` ignores the bool). Harmless
   (the Calendar banner remains), but silent. (b) If the write throws (Pro lapsed / enrollment ended during the
   drawing), the moment stays up celebrating a cross that was never saved. Smallest fix for both: log/handle a
   `false` from `finish()`; optionally offer Finish only after `hooks.Write` completes.
3. **"Program finished" headline vs. a program that is not finished.** The moment's headline reads "Program finished"
   (`CrossMomentMilestoneProgram`) above a *Finish program* / *Keep it open* choice; after *Keep it open* it is not
   finished. Copy only, e.g. "Every day done" / "Program complete".
4. **Automation names say "Minutes: 0" for workouts with no known length.** `StatsWeekName` / `StatsKindName`
   always include `{2}` minutes (`StatsViewModel.Show`), contradicting the ruling that time is shown only where known
   (the visible UI hides it). Drop the minutes clause when 0.
5. **Free sees "Workouts by kind".** Plan Task 3 lists minutes-by-kind as Pro; the implementation shows it to Free
   (`docs/statistics.md` table says so too). More generous, not harmful, but there is no `Ruling:` line for it.
6. **Phase heading dedupe only works in English.** `ProgramCalendarViewModel.RebuildWeeks` (≈ line 492) compares the
   catalog phase name ("Week 1", English, not localized) with the localized week label. In Czech the label is
   "Týden 1", so 21 Day Fix still shows "Week 1" + "Týden 1". Compare against the invariant/English week label as
   well, or against `$"Week {number}"`.
7. **`TextLimit.Truncate` protects surrogate pairs, not text elements.** Plan Polish 1 says "cut on a text-element
   boundary"; `TextLimit.cs:12-14` only avoids a lone high surrogate. ZWJ sequences, flags, keycaps and skin-tone
   modifiers can still be cut mid-cluster (valid UTF-16, broken glyph). Not data corruption; `StringInfo` /
   `GetTextElementEnumerator` would match the plan.
8. **A completed program that went read-only (Pro lapsed, non-primary) can be ended but never *finished*.**
   `FinishAsync` uses `RequireWritableAsync` (`ActiveEnrollmentService.cs:210`) while `EndAsync` uses
   `RequireAsync`. The plan asked for this (`Finish_ReadOnlyEnrollment_Throws`), but it sits awkwardly with the Global
   Constraint "finishing a program … is Free" and the listing line "finishing a program" is free: the lapsed user
   can only record a completed program as ended, not Finished. Worth a conscious decision; finishing writes no
   tracking data.
9. **Tab-lands-on-today only works when today's week is realized.** `WeeksRepeater_GettingFocus` uses
   `WeeksRepeater.TryGetElement(week)`; if the user scrolled far from today, the redirect silently does nothing and
   Tab lands on the first realized cell. `GetOrCreateElement` (as `FocusCell` does) would cover it.
10. **Test that cannot fail on its main claim:** `StatsServiceTests.Free_ReadsOnlyPrimaryAndLast28Days`
    (lines ≈ 34-49). The old program's only cross is 40 days ago, so the 28-day date filter drops it even if Free
    read every enrollment. "Only the primary" is untested. Cross the old program inside the last 28 days (start it 10
    days ago, replace 3 days ago).
11. **Mislabeled test:** `MilestoneDetectorTests.Detect_WeekCompletedEarlier_ReturnsNull` crosses into a week that is
    still open afterwards, so it returns null whether or not the `before` transition is checked. The transition guard
    *is* covered, by `Detect_OptionalSessionOnACompleteWeek_ReturnsNull`; rename or rewrite the first one.
12. **Tests set `CultureInfo.CurrentCulture` in constructors without restoring it** (`StatsServiceTests`,
    `StatsViewModelTests`). Order-dependent culture leaks into later tests on the same thread; save/restore in
    `[TestCleanup]`.
13. **Docs vs. behaviour:** `docs/accessibility.md` states the Statistics names work (see I2) and lists the cross
    moment's Escape/Enter/Space as "dismiss" — with the Finish offer up, Escape and a backdrop tap act as *Keep it
    open*. Fine behaviour; say so in the doc.

## Could not verify

- **Focus jumps to today after a dialog or the moment closes.** `WeeksRepeater_GettingFocus` redirects any
  keyboard-device focus arriving from outside the grid. If WinUI reports `InputDevice == Keyboard` when a
  `ContentDialog` (day sheet, opened with Enter on a crossed day, closed with Esc) restores focus, or when
  `CrossMomentOverlay.Dismiss` calls `_previousFocus.Focus(Programmatic)` after an Esc/Enter dismissal, focus moves
  from the day the user was on to today. Confirm by keyboard: arrow to a past crossed day, Enter, Esc. Cheap guard
  either way: redirect only when `args.Direction is FocusNavigationDirection.Next or Previous`.
- **Release (trimmed) package for Task 2.** Ledger: not checked for the InfoBar, `ConfettiBurst`, `FinishPanel`.
  With `TrimMode=partial` and no new collection type handed to a control, I see no trimming risk, but the plan's
  Global Constraint asks for the packaged smoke path.
- **Enter/Space on *Finish program* on Uno heads.** On WinUI `ButtonBase` marks Enter/Space KeyDown handled, so
  `Overlay_KeyDown` never sees it. If an Uno head lets Space KeyDown bubble, `Overlay_KeyDown` dismisses (Keep it
  open) before the KeyUp click. Confirm on Desktop with the keyboard.
- **Hero art on non-Windows heads.** Same `Content`/`Link` + `ms-appx:///` mechanism as the bundled catalog JSON
  (which works), so likely fine; but a bundled image that fails to decode leaves the hero blank (no gradient
  fallback for bundled paths in `ProgramHero.UpdateBackdrop`), now for all 21 programs at once. Ledger: only
  Windows dark checked.
- **x:Bind OneTime behaviour on Uno heads** for I1 (Uno's x:Bind generator may or may not re-evaluate). WinUI is
  certain; Desktop/WASM not checked.

## Review Focus coverage

| # | Focus | Pinned by | Verdict |
|---|---|---|---|
| 1 | Last session crossed with an earlier day empty → no program milestone, no Finish | `MilestoneDetectorTests.Detect_LastDayCrossedWithEarlierDayEmpty_ReturnsWeekOnly`; `ActiveEnrollmentServiceTests.Finish_DayMissing_ReturnsFalseAndStaysActive`; `ProgramCalendarViewModelTests.Finish_ProgramNotComplete_DoesNothing`; `CrossingCoordinatorTests.Cross_CompletesOnlyAWeek_DoesNotOfferToFinish` | **Pinned** |
| 2 | Sync / past-day cross: milestone only for the user's own cross, only on an incomplete→complete transition | Transition: `Detect_OptionalSessionOnACompleteWeek_ReturnsNull` (real), `Detect_WeekCompletedEarlier_ReturnsNull` (weak, Minor 11). Own-cross only: structural — `MilestoneDetector.Detect` is called only from `CrossingCoordinator` (the day sheet does not cross). Past day under OnlyToday: `Cross_OnlyTodayPastDay_WeekMilestone_ShowsMoment`. | **Pinned** for the transition; sync path by construction, no explicit test |
| 3 | Optional sessions and rest days don't hold a week open | `Detect_OnlyOptionalAndRestLeft_WeekIsComplete`, `IsProgramComplete_EveryRequiredSessionCrossed_IsTrue`, `CanFinish_EveryRequiredSessionCrossed_IsTrue` | **Pinned** |
| 4 | No data / one day / a year: empty state, a single bar, heatmap capped at 26 weeks | `StatsCalculatorTests.Build_NoEntries_ZeroFilledBucketsAndZeroTotals`, `Build_OneEntry_OneNonEmptyBar`, `Build_HeatmapIsAlways182Days`, `Build_EntryOlderThanTheBars_StillCountsInTotals`, `Compare_NoScheduledDays_RateIsZero`; `StatsViewModelTests.WeekBars_AllEmpty_HaveNoLength` (fraction 0 + empty state) | **Pinned** |
| 5 | Sunday 23:30 workout across culture first-day / DST / year boundary | `Build_SundayEntry_MondayFirst_CountsInEndingWeek`, `Build_SundayEntry_SundayFirst_CountsInStartingWeek`, `Build_WeekAcrossNewYear_OneBucket`, `StatsServiceTests.Weeks_FollowTheCultureFirstDayOfWeek` | **Pinned for week assignment.** No DST or time-of-day test, but none is needed: sessions count on `Calendar.DateOf` and bonuses on a stored `DateOnly`; no timestamp is converted except `today` and `EndedAt`. |

## Rulings that look wrong

None produces wrong behaviour. One note:

- *"statistics count a crossed session on its calendar day (Calendar.DateOf), not CompletedOn"* — harmless, but its
  stated reason is moot: `ActiveEnrollmentService.CrossAsync` already stores `CompletedOn = Calendar.DateOf(dayIndex)`,
  not the day the cross was drawn, so both give the same date. No change needed.
- *"the moment's Finish program only finishes"* — acceptable, but combined with Minor 2 the user gets no feedback at
  all if the finish fails or returns false.
