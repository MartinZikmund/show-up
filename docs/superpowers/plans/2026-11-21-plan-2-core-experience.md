# Show Up — Plan 2: Core Experience Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** The app you can actually use offline: bundled verified catalog, a themed responsive shell, a paper calendar where finished sessions are crossed off with an animated marker X (sound + haptics), a Today page with bonus-workout logging, and program browse → detail → start.

**Architecture:** View models and all logic in `ShowUp.Core` (testable), views and platform services in the head. The catalog ships inside the app as JSON assets read through `IBundledCatalogSource`; Supabase delivery comes in a later plan behind the same `ICatalogService`. The marker X geometry is generated in Core (pure, seeded) and rendered by a head `MarkerCrossControl` with two `Path`s animated via `StrokeDashOffset`.

**Tech Stack:** Uno.Sdk 7.1 dev, WinUI XAML, CommunityToolkit.Mvvm, existing `ITrackingRepository` / `ProgressCalculator` / `StreakCalculator` / `CatalogParser` from Plan 1.

**Spec:** `docs/superpowers/specs/2026-11-21-show-up-design.md` (sections "UX & shell", "The cross", "Catalog"). Visual reference: the marker-cross playground (`docs/prototypes/marker-cross.html`, committed in Task 1).

## Global Constraints

- All Plan 1 global constraints apply (template rules, C# style, AwesomeAssertions tests, TimeProvider, `MSBUILDDISABLENODEREUSE=1` on hangs, no blanket process kills, uno-issues recording).
- `/winui-design` rules: `{ThemeResource}` at usage sites; theme dictionaries keyed `Light`, `Dark`, `HighContrast` (never `Default`); HC uses only the 8 `SystemColor*` brushes; typography via styles (SemiBold, never Bold; min 12px); 4px spacing grid; `ControlCornerRadius`/`OverlayCornerRadius`; `x:Bind` with explicit `Mode`; `AutomationProperties.AutomationId` on every interactive control and `AutomationProperties.Name` on icon-only ones; no `Converter={x:Null}`.
- Localization: every user-facing string in both `Strings/en/Resources.resw` and `Strings/cs/Resources.resw` (Czech translations real, not English copies).
- XAML formatted with `dotnet xstyler -c Settings.XamlStyler -r -d ./src` before committing.
- Responsive breakpoints (spec): `< 640` bottom tab bar + single column; `640–1007` NavigationView LeftCompact; `≥ 1008` NavigationView Left expanded, Today + Calendar side by side on the Today page.
- Theme (spec): light = warm off-white paper surface with subtle grid; dark = deep charcoal chalkboard; default marker color marker-red `#D9372B`; display font **Barlow Condensed** (SIL OFL, bundled) for day numbers and headers; body uses the platform Fluent font.
- Marker cross defaults (from the playground): 180 ms per stroke, 90 ms gap, stroke width 9 (at a 96 px cell; scale with cell size), wobble 4, overshoot 8, faint under-stroke at 1.35× width and 35% opacity, ease-out; seed = completion row id hash so the X looks the same on every device; reduced motion (`UISettings.AnimationsEnabled == false`) → instant X; sound and haptics on by default, each toggleable in Settings.
- Stroke rule (Plan 1 `DayStatus.MarkStrokes`): 1 session → X on completion; 2 sessions → "/" then X; 3+ → ticks per session, X when all done.
- Crossing (spec): today and any past day; future days locked (shake, no write).
- Disclaimer string (verbatim, shown in Programs and in Settings → About): "Show Up is an independent app. It is not affiliated with, endorsed by, or sponsored by Bodi (formerly Beachbody) or any program creator. Program names are trademarks of their respective owners and are used only to identify the workout schedules they describe."
- Free tier gates arrive in Plan 3; in this plan one active enrollment at a time is the behavior (starting a new program asks to end the current one).

## Review Focus

1. **Crossing the wrong session after a schedule shift** — taps map to `(dayIndex, sessionIndex)` via `EnrollmentCalendar`, never by grid position; gap days aren't tappable. Test in Task 5 (CalendarViewModel).
2. **Double-tap on a cell** — second tap while the first write is in flight must not cross two sessions. Test in Task 5.
3. **Day rollover while the app stays open** (midnight) — Today/Calendar refresh "today" on resume/activation and when the date changes. Test in Task 5/6 with FakeTimeProvider.
4. **Bonus workout on a day with no enrollment** — allowed; Today shows it; Calendar shows the sticker only inside the enrollment range. Test in Task 6.
5. **Catalog program whose workout id is missing from the bundled workouts** — `CatalogService` reports and skips that program instead of crashing (CI validation should prevent it, but OTA catalogs later won't be validated by CI). Test in Task 2.

---

### Task 1: Land the verified catalog (closes #8)

**Files:** `catalog/workouts.json`, `catalog/programs/*.json`, `catalog/VERIFICATION.md` (from the Opus verifier's VERIFY.md + per-family NOTES sources, condensed), `docs/catalog.md`, `docs/README.md` (one line), `docs/prototypes/marker-cross.html`.

- [ ] **Optional sessions (verifier finding: T25's second Friday workout is printed as optional).** Add `bool Optional = false` to `ScheduledSession` (JSON `optional`, schema + both validators), and make `DayStatus` ignore optional sessions for `Progress`/`MarkStrokes`/streaks (they can still be crossed and show a tick). Test first: `MarkStrokes_OptionalSecondSessionUndone_IsTwo`, `Progress_OnlyOptionalDone_IsEmpty`. Mark T25's optional Friday sessions `optional: true`.
- [ ] Add `heroImage` to `required` in `catalog/schema/program.schema.json` (parser now requires the key; final-review minor).
- [ ] Copy the verified `_merged` output (held programs excluded) into `catalog/`.
- [ ] Run `dotnet test tests/ShowUp.Core.Tests/ShowUp.Core.Tests.csproj --filter "FullyQualifiedName~CatalogFolder"` — Expected: PASS (the C# validator agrees with the research validator).
- [ ] `docs/catalog.md`: how programs are verified (official vs community), file format, how to propose a correction (issue template link), list of programs with verification level and held programs with what's missing.
- [ ] Open follow-up issues for held programs (P90X Doubles, Core De Force Hybrid/Deluxe, T25 Gamma, 80 Day Obsession, plank/push-up challenges, Hip Hop Abs 30-day) labelled `catalog`.
- [ ] Commit `feat(catalog): verified program calendars`, PR `Closes #8`.

### Task 2: Catalog service with bundled source

**Files:**
- Create: `src/ShowUp.Core/Catalog/ICatalogService.cs`, `CatalogService.cs`, `IBundledCatalogSource.cs`
- Create: `src/ShowUp/Services/Catalog/BundledCatalogSource.cs`
- Modify: `src/ShowUp/ShowUp.csproj` (link `..\..\catalog\**\*.json` as `Content` → `Assets/Catalog/%(RecursiveDir)%(Filename)%(Extension)`), `App.xaml.cs` registrations
- Test: `tests/ShowUp.Core.Tests/Catalog/CatalogServiceTests.cs`

**Interfaces:**
- Produces:
  - `interface IBundledCatalogSource { Task<string> ReadWorkoutsAsync(); Task<IReadOnlyList<string>> ReadProgramsAsync(); }` (head: reads `ms-appx:///Assets/Catalog/...` via `StorageFile.GetFileFromApplicationUriAsync`; program file list from a generated `Assets/Catalog/index.json` array of file names — create it with an MSBuild `WriteLinesToFile` target or commit `catalog/index.json` kept in sync by the CI catalog test)
  - `interface ICatalogService { Task<WorkoutCatalog> GetCatalogAsync(); Task<WorkoutProgram?> GetProgramAsync(string programId, int? revision = null); Task<Workout?> GetWorkoutAsync(string workoutId); }` — `revision` null → latest; enrollments resolve their pinned `ProgramRevision`. Parses each program with `CatalogParser.TryParseProgram` (Plan 1 hardening) so one bad file is skipped and logged via `ILogger<CatalogService>`, then validates; caches `WorkoutsById` and `ProgramTrack.Days` once (records recompute them per access).
- [ ] Tests: `GetCatalog_LoadsOnce_Caches`, `GetCatalog_ProgramWithUnknownWorkout_IsSkipped`, `GetProgram_UnknownId_ReturnsNull`.
- [ ] Implement, register singleton, commit, PR.

### Task 3: Theme, fonts and shell

**Files:**
- Modify: `src/ShowUp/Resources/Colors.xaml` (rewrite: `Light`/`Dark`/`HighContrast` dictionaries; keys `PaperBackgroundBrush`, `PaperGridLineBrush`, `CalendarCellBackgroundBrush`, `CalendarCellBorderBrush`, `CalendarCellTodayBorderBrush`, `MarkerBrush` (default marker red; chalk white in Dark), `BonusStickerBackgroundBrush`, `BonusStickerForegroundBrush`, `MutedTextBrush`; plus `AccentColor` override to marker red), `Styles.xaml` (`DisplayNumberTextBlockStyle`, `SectionHeaderTextBlockStyle` using Barlow Condensed SemiBold)
- Create: `src/ShowUp/Assets/Fonts/BarlowCondensed-SemiBold.ttf`, `BarlowCondensed-Bold.ttf`, `OFL.txt` (download from the Google Fonts GitHub repo `google/fonts/ofl/barlowcondensed`)
- Modify: `src/ShowUp.Core/Navigation/NavigationSection.cs` (`Today, Calendar, Programs, History, Settings`), `WindowShell.xaml(.cs)`, `WindowShellViewModel.cs`
- Create: `src/ShowUp/Controls/BottomTabBar.xaml(.cs)` (5 tabs, icon + label, selected state uses `MarkerBrush` underline; `AutomationId`s `Tab_Today` …), placeholder pages `TodayView`, `CalendarView` (name: `ProgramCalendarView` to avoid clashing with WinUI `CalendarView`), `ProgramsView`, `HistoryView` with their VMs (empty "coming soon" for History)
- Test: `tests/ShowUp.Core.Tests/ViewModels/WindowShellViewModelTests.cs` (section ↔ VM mapping, title per section)

- [ ] Shell: `AdaptiveTrigger` visual states on `WindowShell` — `Narrow` (<640): `NavView.PaneDisplayMode=LeftMinimal`, pane toggle hidden, `BottomTabBar` visible in a bottom row (respect `VisibleBoundsPadding` bottom inset); `Medium` (640–1007): `LeftCompact`, bar collapsed; `Wide` (≥1008): `Left`. Remove the template's disabled search box from the title bar. Default landing section: Today.
- [ ] Page background: `PaperBackgroundBrush` with a subtle 24 px grid (tile via a `Rectangle` with an `ImageBrush` of a generated grid PNG, or `Canvas` lines — builder's call; must not hurt scrolling perf).
- [ ] Settings → About: disclaimer text.
- [ ] Verify on Desktop at 360×740, 800×900, 1400×900 (Uno-app MCP screenshots to `.screenshots/`), WASM at 390 px width (Playwright), and Windows head (`winapp`), light + dark. Attach screenshots to the PR description (upload via `gh` as PR comment images or link committed nothing — screenshots stay out of git).
- [ ] Commit `feat(ui): themed responsive shell`, PR `Closes #7`.

### Task 4: MarkerCrossControl (signature moment)

**Files:**
- Create: `src/ShowUp.Core/Marking/MarkerStroke.cs`, `MarkerGeometry.cs`, `IMarkingFeedback.cs`
- Create: `src/ShowUp/Controls/MarkerCrossControl.cs` (templated control) + `Themes/Generic.xaml` or a style in `Styles.xaml`
- Create: `src/ShowUp/Services/Marking/MarkingFeedback.cs` (+ platform partials as needed), `src/ShowUp/Assets/Sounds/marker-1.wav`, `marker-2.wav`, `marker-3.wav` (CC0 — e.g. from freesound.org CC0 or synthesized offline with a small script committed under `tools/`; record the license in `src/ShowUp/Assets/Sounds/LICENSE.md`)
- Test: `tests/ShowUp.Core.Tests/Marking/MarkerGeometryTests.cs`

**Interfaces:**
- Produces:
  - `readonly record struct MarkerStroke(Point Start, Point Control, Point End)` (`Windows.Foundation.Point`)
  - `static class MarkerGeometry { (MarkerStroke First, MarkerStroke Second) Create(int seed, double width, double height, double wobble = 4, double overshoot = 8); }` — algorithm exactly as the playground's `strokePath` (margin = 16% of min side; first stroke top-left→bottom-right, second top-right→bottom-left; seeded PRNG so the same seed gives identical strokes); `int SeedFrom(Guid id)` stable across platforms (don't use `GetHashCode`).
  - `interface IMarkingFeedback { void StrokeDrawn(); void SessionTicked(); bool SoundEnabled { get; set; } bool HapticsEnabled { get; set; } }` — persisted via `IAppPreferences`.
  - `MarkerCrossControl` dependency properties: `int Strokes` (0–2), `int Seed`, `Brush MarkerBrush`, `double StrokeThicknessScale` (default 1), `bool AnimateChanges` (default true). Rendering: per stroke two `Path`s (main at width `9 × min(w,h)/96`, 90% opacity; under-stroke 1.35× width, 35% opacity), round caps; when `Strokes` increases and `AnimateChanges` and animations are enabled, animate `StrokeDashOffset` from length → 0 over 180 ms (ease-out), second stroke 90 ms after the first when going 0→2, call `IMarkingFeedback.StrokeDrawn()` at each stroke start (resolve via `App.Services` in the head control — acceptable here as the control is head-only). Decreasing `Strokes` → immediate redraw without animation.
- [ ] Tests: `Create_SameSeed_IsDeterministic`, `Create_DifferentSeeds_Differ`, `Create_StrokesStayNearCellBounds` (within overshoot + wobble of the box), `SeedFrom_SameGuid_SameSeed`.
- [ ] Sound: `MediaPlayer` (enable the Uno `MediaPlayer` feature if required on Skia/WASM — check Uno docs MCP for 7.x: `UnoFeatures` `MediaPlayerElement`/`MediaPlayer`) with a small pool, random variant per stroke; if a head can't play audio, no-op and note it in `docs/marking.md`. Haptics: `Windows.Phone.Devices.Notification.VibrationDevice.GetDefault()?.Vibrate(TimeSpan.FromMilliseconds(12))` on Android/iOS (check Uno support; Android needs `VIBRATE` permission); no-op elsewhere.
- [ ] Verify the animation and sound by hand on Desktop + WASM + Windows (record a GIF/short screenshot sequence into `.screenshots/`), check `StrokeDashOffset` animation works on Uno Skia — if not, fall back to animating a clip or `Path.Data` trim; record any Uno gap in uno-issues.
- [ ] `docs/marking.md`, commit `feat(ui): marker cross control with sound and haptics`, PR `Closes #10` (partially — calendar page in Task 5 completes it; use `Refs #10` here).

### Task 5: Program calendar page

**Files:**
- Create: `src/ShowUp.Core/ViewModels/ProgramCalendarViewModel.cs`, `CalendarDayViewModel.cs`, `src/ShowUp.Core/Tracking/IActiveEnrollmentService.cs`, `ActiveEnrollmentService.cs`
- Modify: `src/ShowUp/Views/ProgramCalendarView.xaml(.cs)`
- Test: `tests/ShowUp.Core.Tests/ViewModels/ProgramCalendarViewModelTests.cs`, `Tracking/ActiveEnrollmentServiceTests.cs`

**Interfaces:**
- Consumes: `ITrackingRepository`, `ICatalogService`, `EnrollmentCalendar`, `ProgressCalculator`, `StreakCalculator`, `TimeProvider`, `IMarkingFeedback`.
- Produces:
  - `interface IActiveEnrollmentService { Task<ActiveEnrollment?> GetAsync(); event EventHandler? Changed; }` with `sealed record ActiveEnrollment(EnrollmentEntity Enrollment, WorkoutProgram Program, ProgramTrack Track, EnrollmentCalendar Calendar)`; raises `Changed` after enroll/end/cross/uncross/shift/bonus (via `WeakReferenceMessenger` message `TrackingChangedMessage` in `ShowUp.Core.Messages`). **It is the only path that crosses/uncrosses/shifts:** `Task CrossAsync(int dayIndex, int sessionIndex)` computes `dayDate = Calendar.DateOf(dayIndex)` itself; `Task UncrossAsync(int dayIndex, int sessionIndex)`; `Task ShiftAsync(DateOnly fromDate, int days)` (passes `Track.Days.Count`; repository clamps so crossed days never move); `Task<IReadOnlyList<CompletedSession>> GetCompletionsAsync()`; enrollment's program resolved with its pinned revision.
  - `CalendarDayViewModel : ObservableObject` — `int DayIndex`, `DateOnly Date`, `int DayNumber` (1-based), `IReadOnlyList<SessionItem> Sessions` (`sealed record SessionItem(int Index, string Name, bool IsDone)`), `int MarkStrokes`, `bool ShowsTicks`, `bool IsToday`, `bool IsFuture`, `bool IsMissed`, `bool IsRestDay`, `int BonusCount`, `int Seed` (`MarkerGeometry.SeedFrom` of the deterministic first `CompletedSession.CompletionId` of that day; `DayIndex` if none), `bool IsLate` (any session `CompletedOn > Date`), `string DateLabel`.
  - `ProgramCalendarViewModel` — `ObservableCollection<CalendarWeekViewModel> Weeks` (`PhaseName`, `WeekNumber`, `IReadOnlyList<CalendarDayViewModel> Days`), `int CurrentStreak`, `int LongestStreak`, `string ProgramName`, `bool HasEnrollment`; `[RelayCommand] Task CrossNextAsync(CalendarDayViewModel day)` (future → no write, sets `day.ShakeRequested`; otherwise crosses the lowest undone session index; ignores re-entry while a write for that day is in flight), `[RelayCommand] Task UncrossLastAsync(CalendarDayViewModel day)` (confirmation via `IConfirmationDialogService`), `[RelayCommand] Task ShiftScheduleAsync()` (from today, 1 day — UI asks days count via a small dialog), refreshes on `TrackingChangedMessage` and on window activation/date change.
- [ ] Tests (FakeTimeProvider, in-memory fakes for repository/catalog): `CrossNext_FutureDay_DoesNotWrite`, `CrossNext_TwoSessionDay_FirstTapDrawsOneStroke`, `CrossNext_DoubleTap_CrossesOnce`, `CrossNext_AfterShift_MapsToShiftedDay`, `Load_TodayPastMidnight_RefreshesIsToday`, `Load_NoEnrollment_HasEnrollmentFalse`.
- [ ] Layout ruling: rows are **program weeks** (Day N primary, date as a small secondary label); gap days from shifts are not drawn. Use `ProgramTrack.Locate(dayIndex)` for phase/week.
- [ ] View: phases as headers, weeks as rows of 7 equal cells (`Grid` with 7 star columns per week inside an `ItemsRepeater` of weeks — cells must stay square-ish; at < 640 cells shrink, workout names wrap to 2 lines max with `CaptionTextBlockStyle`, at < 400 width show only the day number + session ticks/abbreviated name). Each cell is a `Button`-derived template (keyboard + screen reader: `AutomationProperties.Name` = "Day 5, Pure Cardio and Cardio Abs, 1 of 2 done") hosting `MarkerCrossControl` overlay; right-click / long-press (`ContextFlyout`) → "Uncross", "Log bonus workout here". Bonus sticker top-right. Header: program name, streak "🔥 N-day streak" (localized), Shift schedule button. Empty state when no enrollment → button to Programs.
- [ ] Verify on Desktop/WASM/Windows at narrow + wide, light + dark; screenshots in PR. Commit, PR `Closes #10`.

### Task 6: Today page + bonus workouts

**Files:**
- Create: `src/ShowUp.Core/ViewModels/TodayViewModel.cs`, `BonusWorkoutDialogViewModel.cs`
- Modify: `src/ShowUp/Views/TodayView.xaml(.cs)`; create `src/ShowUp/Views/BonusWorkoutDialog.xaml(.cs)` (ContentDialog)
- Test: `tests/ShowUp.Core.Tests/ViewModels/TodayViewModelTests.cs`, `BonusWorkoutDialogViewModelTests.cs`

**Interfaces:**
- Produces: `TodayViewModel` — `string Greeting` (by time of day), `string? ProgramName`, `int? DayNumber`, `IReadOnlyList<TodaySessionViewModel> Sessions` (`Name`, `DurationMinutes`, `Kind`, `IsDone`, `CrossCommand`), `IReadOnlyList<BonusWorkoutEntity> Bonuses`, `int CurrentStreak`, `bool IsRestDay`, `[RelayCommand] Task LogBonusAsync()` (opens dialog via a view-service interface `IBonusWorkoutDialog` in Core), `[RelayCommand] Task RemoveBonusAsync(BonusWorkoutEntity)`.
  - `BonusWorkoutDialogViewModel` — `string Query` → `IReadOnlyList<Workout> Suggestions` (all catalog workouts, matched by name, case-insensitive, family shown, max 20), `Workout? Selected`, `string CustomTitle`, `int? DurationMinutes` (prefilled from workout), `DateOnly Date` (default today, past allowed, future not), `bool CanSave`.
- [ ] Domain rule in Core (final-review minor): `SqliteTrackingRepository.LogBonusAsync` rejects future dates (`CrossingPolicy.CanCross`) and blank titles with `ArgumentException`; test `LogBonus_FutureDate_Throws`, `LogBonus_BlankTitle_Throws` (fix commit, then test commit).
- [ ] Tests: `Load_RestDay_ShowsRestState`, `Cross_FromToday_CrossesCorrectSession`, `Bonus_NoEnrollment_StillLogs`, `Dialog_Search_AbAttack_FindsMax30Workout` (catalog fixture contains `insanity-max-30-ab-attack-10`), `Dialog_FutureDate_CannotSave`.
- [ ] View: ≥1008 two columns (Today 360–420 px fixed left, calendar `ProgramCalendarView` content to the right — reuse the calendar as a `UserControl`), narrower single column. Session cards: big "Cross it off" button that plays the X on the card (reuse `MarkerCrossControl`), done state. Bonus list with "+ Bonus workout" button. Verify + screenshots. Commit, PR `Closes #11`.

### Task 7: Programs browse, detail and start

**Files:**
- Create: `src/ShowUp.Core/ViewModels/ProgramsViewModel.cs`, `ProgramDetailViewModel.cs`
- Modify: `src/ShowUp/Views/ProgramsView.xaml(.cs)`; create `src/ShowUp/Views/ProgramDetailView.xaml(.cs)`, `src/ShowUp/Controls/ProgramHero.xaml(.cs)` (hero image if `HeroImage` set, else gradient from `AccentColor` with the program name in Barlow Condensed — this is the #9 fallback)
- Test: `tests/ShowUp.Core.Tests/ViewModels/ProgramsViewModelTests.cs`, `ProgramDetailViewModelTests.cs`

**Interfaces:**
- Produces: `ProgramsViewModel` — `IReadOnlyList<ProgramCardViewModel> Programs` (`Id`, `Name`, `Family`, `TotalDays` (longest track), `TrackCount`, `AccentColor`, `HeroImage`, `Verification`), `string Filter`, `IReadOnlyList<string> Families`, `string? SelectedFamily`, navigate to detail with program id.
  - `ProgramDetailViewModel` — `OnNavigatedTo(object? programId)`; `WorkoutProgram Program`, `ProgramTrack SelectedTrack`, `IReadOnlyList<WeekPreview>` (week rows of workout names), `DateTimeOffset StartDate` (default today), `[RelayCommand] Task StartAsync()` — if an active enrollment exists, confirm "End {current} and start {new}?" (`EnrollmentEndReason.Abandoned`), then `EnrollAsync(program.Id, program.Revision, track.Id, start)` and navigate to Calendar. Verification badge: official vs "community-verified calendar — report an issue" link to GitHub issue template (`https://github.com/MartinZikmund/show-up/issues/new?template=...`).
- [ ] Tests: `Filter_ByName_IsCaseInsensitive`, `Start_WithActiveEnrollment_EndsPreviousAfterConfirm`, `Start_Declined_KeepsPrevious`, `Detail_DefaultTrack_IsFirst`.
- [ ] View: `GridView`/`ItemsRepeater` with `UniformGridLayout` cards (MinItemWidth 280), filter `AutoSuggestBox` + family `ComboBox`; detail page hero + track `SelectorBar` (if >1 track) + week preview + `CalendarDatePicker` + Start button; disclaimer footer. Verify + screenshots. Commit, PR `Closes #12` and `Refs #9`.

---

## Following plans

3. Entitlements (Free/Pro, Uno.RevenueCat) + reminders per platform
4. Custom schedule builder
5. Supabase schema + auth + sync + catalog publishing
6. Hero art, history/stats, celebrations, accessibility pass, store metadata
