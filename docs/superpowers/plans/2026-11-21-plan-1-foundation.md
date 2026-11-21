# Show Up — Plan 1: Foundation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A public `MartinZikmund/show-up` repo generated from Martin's Uno template on Uno.Sdk 7.1 dev, CI green, with the Core domain (catalog model, schedule engine, progress/streaks, SQLite storage) built test-first and SQLite proven on WASM.

**Architecture:** Single-project Uno app (`src/ShowUp`) + UI-free `src/ShowUp.Core` + MSTest `tests/ShowUp.Core.Tests`. All domain logic in Core, pure and unit-tested; storage via sqlite-net behind repository interfaces. Later plans (shell/theme, calendar + marker cross, catalog research, entitlements, reminders, builder, Supabase sync) build on these interfaces.

**Tech Stack:** .NET 11, Uno.Sdk `7.1.0-dev.36`, CommunityToolkit.Mvvm, System.Text.Json source generation, sqlite-net-e + SourceGear.sqlite3, MSTest (MTP) + FluentAssertions.

**Spec:** `docs/superpowers/specs/2026-11-21-show-up-design.md`

## Global Constraints

- Generated with `dotnet new mzuno-app` from `MartinZikmund/uno-app-template` `origin/main`; heads: android, ios, windows, desktop, wasm (no tvOS).
- Names: solution/namespaces `ShowUp`, `ShowUp.Core`; title `Show Up`; appId `dev.mzikmund.showup` (Dev channel appends `.dev` automatically); publisher `Martin Zikmund`; githubRepo `MartinZikmund/show-up`.
- `global.json` → `"Uno.Sdk": "7.1.0-dev.36"` (or the newest `7.1.0-dev.*` on nuget.org at execution time).
- Template rules in `AGENTS.md` / `.claude/rules/*` apply: no new Uno.Extensions Navigation/MVUX/C# Markup; prefer CommunityToolkit/WinUI over Uno Toolkit/Themes; `WarningsAsErrors`; Central Package Management.
- Martin's C# style: file-scoped namespaces, nullable, explicit type + target-typed `new()`, braces always, expression bodies for one-liners, terse comments.
- Tests: `Method_Scenario_ExpectedResult`, AAA, FluentAssertions, hand-written fakes over Moq. Run: `dotnet test tests/ShowUp.Core.Tests/ShowUp.Core.Tests.csproj`.
- If a `dotnet build/test` hangs with orphaned MSBuild nodes, rerun with env `MSBUILDDISABLENODEREUSE=1`. Never blanket-kill dotnet processes.
- Check `git config core.hooksPath` before running formatters; `dotnet format` changed files otherwise.
- Every task: its own branch `feature/<issue#>-<slug>`, PR that `Closes #<issue>`, CI green, squash-merge. Commit message trailer per session attribution.
- Time is injected via `TimeProvider` (BCL); dates are `DateOnly` in device local time.
- Uno bugs discovered → record in `C:\Users\Martin\.claude\uno-issues\uno--<kebab-title>.md` (format in Martin's CLAUDE.md).

## Review Focus

1. **Schedule shift crossing a phase boundary / applied twice** — dates after the shift move by the sum of shifts; days before `FromDate` don't move. Test in Task 4.
2. **Duplicate completion rows for the same session (two devices)** — count once; uncross tombstones all of them. Test in Task 6.
3. **Crossing a future day** — rejected with `InvalidOperationException`; today and any past day allowed. Test in Tasks 4 and 6.
4. **Day with 3+ sessions partially done** — `MarkStrokes == 0`, `ShowsSessionTicks == true`; full → 2. Test in Task 5.
5. **Streak when today isn't done yet / rest day uncrossed** — today pending doesn't break; uncrossed rest day neither adds nor breaks. Test in Task 5.

---

### Task 1: Generate repo, bump to Uno 7.1 dev, publish

**Files:**
- Create: entire repo at `D:\Personal\show-up` (generated), keep existing `docs/superpowers/specs/*` and `docs/superpowers/plans/*`
- Modify: `global.json`, `README.md`

**Interfaces:**
- Produces: buildable `src/ShowUp.slnx`, `src/ShowUp/ShowUp.csproj`, `src/ShowUp.Core/ShowUp.Core.csproj`, `tests/ShowUp.Core.Tests/ShowUp.Core.Tests.csproj`; public GitHub repo with `main`.

- [ ] **Step 1: Install the template from origin/main**

```bash
cd D:/Personal/uno-app-template && git fetch origin
mkdir -p "$SCRATCH/tpl" && git archive origin/main | tar -x -C "$SCRATCH/tpl"
dotnet new install "$SCRATCH/tpl"
```
Expected: `mzuno-app` listed by `dotnet new list mzuno`.

- [ ] **Step 2: Generate into a staging folder and merge in**

```bash
dotnet new mzuno-app -n ShowUp -o "$SCRATCH/gen" --title "Show Up" --appId dev.mzikmund.showup \
  --publisher "Martin Zikmund" --githubRepo MartinZikmund/show-up \
  --platforms android --platforms ios --platforms windows --platforms desktop --platforms wasm --specKit false
cp -rn "$SCRATCH/gen/." D:/Personal/show-up/
```
Expected: `D:/Personal/show-up/src/ShowUp.slnx` exists; spec and plan files untouched.

- [ ] **Step 3: Bump Uno.Sdk** — set `msbuild-sdks."Uno.Sdk"` in `global.json` to the newest `7.1.0-dev.*` (`curl -s https://api.nuget.org/v3-flatcontainer/uno.sdk/index.json`).

- [ ] **Step 4: Verify build + tests**

Run: `dotnet build src/ShowUp/ShowUp.csproj -f net11.0-desktop` (use the desktop TFM from the csproj) and `dotnet test tests/ShowUp.Core.Tests/ShowUp.Core.Tests.csproj`
Expected: build succeeds with 0 warnings; tests pass. Also build `-f <browserwasm TFM>` and `-f <windows TFM>`. Breakage from 7.1 → fix or record per Global Constraints.

- [ ] **Step 5: README** — replace the template README body with: one-paragraph pitch, "Built in public" note, heads list, build commands (link `docs/building.md`), and the disclaimer verbatim: *"Show Up is an independent app. It is not affiliated with, endorsed by, or sponsored by Bodi (formerly Beachbody) or any program creator. Program names are trademarks of their respective owners and are used only to identify the workout schedules they describe."*

- [ ] **Step 6: git init, commit, create public repo, push**

```bash
cd D:/Personal/show-up && git init -b main && git add -A
git commit -m "chore: bootstrap Show Up from uno-app-template on Uno.Sdk 7.1 dev"
gh repo create MartinZikmund/show-up --public --source . --push --description "Show Up — cross off your home workouts like a marker on a paper calendar. Uno Platform, built in public."
```

- [ ] **Step 7: File the roadmap issues** — one issue per remaining milestone (spec "Milestones" 2–11, plus Tasks 2–7 of this plan as individual issues), plus `Achievements`, `Statistics`, `Community` labelled `future`. Labels: `milestone-N`, `future`, `catalog`, `ui`, `core`, `infra`.

### Task 2: CI green on all heads

**Files:**
- Modify: `.github/workflows/*.yml` only as needed

**Interfaces:**
- Consumes: Task 1 repo.
- Produces: green `ci.yml` run on `main` covering build of all five heads + Core tests.

- [ ] **Step 1:** `gh run list --limit 5` after Task 1 push; inspect failures with `gh run view <id> --log-failed`.
- [ ] **Step 2:** Fix workflow issues (workload restore for Uno 7.1 dev / .NET 11, Xcode selection) on branch `feature/<issue>-ci-green`; PR `Closes #<issue>`.
- [ ] **Step 3:** Expected: all jobs green on the PR; merge with `gh pr merge --squash --delete-branch`.

### Task 3: Catalog model, JSON and validation

**Files:**
- Create: `src/ShowUp.Core/Catalog/WorkoutKind.cs`, `Workout.cs`, `WorkoutProgram.cs` (records below), `CatalogJsonContext.cs`, `CatalogParser.cs`, `CatalogValidator.cs`
- Create: `catalog/workouts.json`, `catalog/schema/program.schema.json`, `catalog/programs/.gitkeep`
- Test: `tests/ShowUp.Core.Tests/Catalog/CatalogParserTests.cs`, `CatalogValidatorTests.cs`, fixture `tests/ShowUp.Core.Tests/Catalog/Fixtures/sample-program.json` + `sample-workouts.json` (copied to output)

**Interfaces:**
- Produces (namespace `ShowUp.Core.Catalog`):
  - `enum WorkoutKind { Cardio, Strength, Abs, Yoga, Stretch, Recovery, Rest, FitTest, Mixed }`
  - `enum VerificationLevel { Official, Community }`
  - `sealed record Workout(string Id, string Name, int? DurationMinutes, WorkoutKind Kind, string? Family)`
  - `sealed record ScheduledSession(string WorkoutId, IReadOnlyList<string>? Alternates = null)`
  - `sealed record ScheduleDay(IReadOnlyList<ScheduledSession> Sessions)` with `bool IsRestDay(IReadOnlyDictionary<string, Workout> workouts)` (all sessions are `Rest`)
  - `sealed record ScheduleWeek(IReadOnlyList<ScheduleDay> Days)`
  - `sealed record ProgramPhase(string Name, IReadOnlyList<ScheduleWeek> Weeks)`
  - `sealed record ProgramTrack(string Id, string Name, IReadOnlyList<ProgramPhase> Phases)` with `IReadOnlyList<ScheduleDay> Days` (flattened, phase→week→day order)
  - `sealed record CatalogSource(string Url, string Confirms)`
  - `sealed record WorkoutProgram(string Id, int Revision, string Name, string Family, string Description, string? HeroImage, string AccentColor, VerificationLevel Verification, IReadOnlyList<CatalogSource> Sources, DateOnly VerifiedOn, IReadOnlyList<ProgramTrack> Tracks)`
  - `sealed record WorkoutCatalog(int Version, IReadOnlyList<Workout> Workouts, IReadOnlyList<WorkoutProgram> Programs)` with `IReadOnlyDictionary<string, Workout> WorkoutsById`
  - `static class CatalogParser { WorkoutCatalog Parse(int version, string workoutsJson, IEnumerable<string> programJsons); }` — camelCase JSON, enums as camelCase strings, via `CatalogJsonContext` (source-generated, AOT-safe for WASM).
  - `static class CatalogValidator { IReadOnlyList<string> Validate(WorkoutCatalog catalog); }` — returns human-readable errors, empty = valid.

- [ ] **Step 1: Write failing tests**

```csharp
[TestMethod]
public void Parse_SampleProgram_FlattensDaysInOrder()
{
    var catalog = CatalogParser.Parse(1, Fixture("sample-workouts.json"), [Fixture("sample-program.json")]);
    var track = catalog.Programs.Single().Tracks.Single();
    track.Days.Should().HaveCount(14);               // fixture: 2 phases × 1 week × 7 days
    track.Days[0].Sessions.Select(s => s.WorkoutId).Should().Equal("fit-test");
    track.Days[1].Sessions.Should().HaveCount(2);    // cardio + abs double
}

[TestMethod]
public void Validate_UnknownWorkoutReference_ReturnsError() { /* session "nope" → error contains "nope" */ }

[TestMethod]
public void Validate_CommunityWithOneSource_ReturnsError() { /* Community needs >= 2 sources */ }

[TestMethod]
public void Validate_OfficialWithoutSource_ReturnsError() { /* Official needs >= 1 source */ }

[TestMethod]
public void Validate_WeekWithEightDays_ReturnsError() { }

[TestMethod]
public void Validate_DuplicateProgramOrWorkoutIds_ReturnsError() { }

[TestMethod]
public void Validate_BadAccentColor_ReturnsError() { /* must match ^#[0-9A-Fa-f]{6}$ */ }

[TestMethod]
public void Validate_SampleCatalog_ReturnsNoErrors() { }
```

- [ ] **Step 2:** Run `dotnet test ... --filter "FullyQualifiedName~Catalog"` — Expected: FAIL (types missing).
- [ ] **Step 3:** Implement the records, parser and validator above; write `program.schema.json` (draft 2020-12) mirroring `WorkoutProgram`.
- [ ] **Step 4:** Run tests — Expected: PASS.
- [ ] **Step 5:** Add a Core test `CatalogFolder_AllFiles_AreValid` that loads `catalog/workouts.json` + every `catalog/programs/*.json` from the repo root (walk up from `AppContext.BaseDirectory` to the folder containing `catalog/`) and asserts `Validate` is empty — this is the CI gate for catalog PRs.
- [ ] **Step 6:** Commit `feat(core): catalog model, parser and validator` and open PR closing the issue.

### Task 4: Enrollment calendar and crossing policy

**Files:**
- Create: `src/ShowUp.Core/Tracking/ScheduleShift.cs`, `EnrollmentCalendar.cs`, `CrossingPolicy.cs`
- Test: `tests/ShowUp.Core.Tests/Tracking/EnrollmentCalendarTests.cs`, `CrossingPolicyTests.cs`

**Interfaces:**
- Produces (namespace `ShowUp.Core.Tracking`):
  - `sealed record ScheduleShift(DateOnly FromDate, int Days)` (Days ≥ 1)
  - `sealed class EnrollmentCalendar(DateOnly startDate, int dayCount, IReadOnlyList<ScheduleShift> shifts)` with `DateOnly DateOf(int dayIndex)`, `int? DayIndexOn(DateOnly date)` (null on gap days created by a shift and outside range), `DateOnly EndDate`.
  - Shift rule: shifts apply in list order; each moves every day whose *current* date `>= FromDate` forward by `Days`.
  - `static class CrossingPolicy { bool CanCross(DateOnly dayDate, DateOnly today) => dayDate <= today; }`

- [ ] **Step 1: Failing tests**

```csharp
[TestMethod] public void DateOf_NoShifts_IsStartPlusIndex()           // start 2026-12-01, idx 9 → 2026-12-10
[TestMethod] public void DateOf_ShiftFromDay3By2_MovesLaterDaysOnly() // idx 1 → 12-02, idx 3 → 12-06
[TestMethod] public void DateOf_TwoShifts_Accumulate()                 // shift(12-04,2) then (12-10,1): idx 8 → 12-12
[TestMethod] public void DayIndexOn_GapDay_ReturnsNull()               // 12-04 after shift(12-04,2) → null
[TestMethod] public void EndDate_WithShift_IncludesShift()             // 90 days + shift 3 → start+92
[DataRow("2026-12-01","2026-12-01",true)] [DataRow("2026-11-01","2026-12-01",true)] [DataRow("2026-12-02","2026-12-01",false)]
[TestMethod] public void CanCross_ReturnsExpected(string day, string today, bool expected)
```

- [ ] **Step 2:** Run → FAIL. **Step 3:** Implement. **Step 4:** Run → PASS. **Step 5:** Commit `feat(core): enrollment calendar with schedule shifts`, PR.

### Task 5: Day progress, marker strokes and streaks

**Files:**
- Create: `src/ShowUp.Core/Tracking/DayStatus.cs`, `ProgressCalculator.cs`, `StreakCalculator.cs`
- Test: `tests/ShowUp.Core.Tests/Tracking/DayStatusTests.cs`, `ProgressCalculatorTests.cs`, `StreakCalculatorTests.cs`

**Interfaces:**
- Consumes: `ProgramTrack.Days`, `ScheduleDay.IsRestDay`, `EnrollmentCalendar`.
- Produces:
  - `enum DayProgress { Empty, Partial, Complete }`
  - `sealed record DayStatus(int DayIndex, DateOnly Date, int SessionCount, IReadOnlySet<int> CompletedSessions, bool IsRestDay)` with `DayProgress Progress`, `int MarkStrokes` (complete → 2; exactly 2 sessions with 1 done → 1; else 0), `bool ShowsSessionTicks => SessionCount >= 3`, `bool IsMissed(DateOnly today)` (date < today, not complete, not rest day).
  - `static class ProgressCalculator { IReadOnlyList<DayStatus> Build(ProgramTrack track, IReadOnlyDictionary<string, Workout> workouts, EnrollmentCalendar calendar, IReadOnlySet<(int Day, int Session)> completed); }`
  - `static class StreakCalculator { int Current(IReadOnlyList<DayStatus> days, DateOnly today); int Longest(IReadOnlyList<DayStatus> days, DateOnly today); }` — walk days with `Date <= today` newest first; today not complete → skip it; rest days skip (no add, no break); complete training day +1; missed training day ends the walk.

- [ ] **Step 1: Failing tests** (spec values):

```csharp
[TestMethod] public void MarkStrokes_SingleSessionDone_IsTwo()
[TestMethod] public void MarkStrokes_TwoSessionsOneDone_IsOne()
[TestMethod] public void MarkStrokes_ThreeSessionsTwoDone_IsZeroWithTicks()
[TestMethod] public void IsMissed_PastUncrossedRestDay_IsFalse()
[TestMethod] public void Build_CompletedOutOfRangeSession_IsIgnored()
[TestMethod] public void Current_TodayPending_DoesNotBreak()           // yesterday+day before done → 2
[TestMethod] public void Current_UncrossedRestDayBetween_KeepsCounting() // done, rest(uncrossed), done → 2
[TestMethod] public void Current_MissedTrainingDay_Breaks()
[TestMethod] public void Longest_TwoRuns_ReturnsLonger()
```

- [ ] **Step 2:** FAIL. **Step 3:** Implement. **Step 4:** PASS. **Step 5:** Commit `feat(core): day progress, marker strokes and streaks`, PR.

### Task 6: SQLite storage and tracking repository

**Files:**
- Create: `src/ShowUp.Core/Storage/SyncableEntity.cs`, `EnrollmentEntity.cs`, `CompletionEntity.cs`, `ScheduleShiftEntity.cs`, `BonusWorkoutEntity.cs`, `ShowUpDatabase.cs`, `Migrations.cs`, `IUserScope.cs`, `IDeviceIdProvider.cs`
- Create: `src/ShowUp.Core/Tracking/ITrackingRepository.cs`, `src/ShowUp.Core/Storage/SqliteTrackingRepository.cs`
- Modify: Core DI registration (template's service-collection extension in Core/head — follow `.claude/rules/architecture.md`)
- Test: `tests/ShowUp.Core.Tests/Storage/SqliteTrackingRepositoryTests.cs`, `MigrationsTests.cs`, fakes `FakeUserScope`, `FakeDeviceIdProvider`, `Microsoft.Extensions.Time.Testing.FakeTimeProvider` (add `Microsoft.Extensions.TimeProvider.Testing` to the test project)

**Interfaces:**
- Produces (namespace `ShowUp.Core.Storage` / `ShowUp.Core.Tracking`):
  - `abstract class SyncableEntity { [PrimaryKey] Guid Id; string? UserId; DateTimeOffset CreatedAt; DateTimeOffset UpdatedAt; DateTimeOffset? DeletedAt; string DeviceId; }` (properties)
  - `EnrollmentEntity : SyncableEntity { string ProgramId; int ProgramRevision; string TrackId; int StartDayNumber; DateTimeOffset? EndedAt; EnrollmentEndReason? EndReason; }` — dates stored as `DateOnly.DayNumber` ints, exposed via `[Ignore] DateOnly StartDate`
  - `CompletionEntity : SyncableEntity { Guid EnrollmentId; int DayIndex; int SessionIndex; int CompletedOnDayNumber; DateTimeOffset CompletedAt; }` + index on `(EnrollmentId, DayIndex, SessionIndex)`
  - `ScheduleShiftEntity : SyncableEntity { Guid EnrollmentId; int FromDayNumber; int Days; }`
  - `BonusWorkoutEntity : SyncableEntity { string? WorkoutId; string Title; int DayNumber; int? DurationMinutes; }`
  - `enum EnrollmentEndReason { Finished, Abandoned }`
  - `interface IUserScope { string? CurrentUserId { get; } }`; `interface IDeviceIdProvider { string DeviceId { get; } }`
  - `sealed class ShowUpDatabase(string databasePath)` : `Task<SQLiteAsyncConnection> GetConnectionAsync()` (lazy, runs `Migrations.ApplyAsync` once; `PRAGMA user_version` = 1 after v1)
  - `interface ITrackingRepository`:
    - `Task<EnrollmentEntity> EnrollAsync(string programId, int revision, string trackId, DateOnly startDate)`
    - `Task<IReadOnlyList<EnrollmentEntity>> GetActiveEnrollmentsAsync()`
    - `Task EndEnrollmentAsync(Guid enrollmentId, EnrollmentEndReason reason)`
    - `Task CrossSessionAsync(Guid enrollmentId, int dayIndex, int sessionIndex, DateOnly dayDate)` — throws `InvalidOperationException` if `!CrossingPolicy.CanCross(dayDate, today)`; no-op if already crossed
    - `Task UncrossSessionAsync(Guid enrollmentId, int dayIndex, int sessionIndex)` — tombstones all matching rows
    - `Task<IReadOnlySet<(int Day, int Session)>> GetCompletedSessionsAsync(Guid enrollmentId)`
    - `Task<IReadOnlyList<ScheduleShift>> GetShiftsAsync(Guid enrollmentId)`; `Task ShiftScheduleAsync(Guid enrollmentId, DateOnly fromDate, int days)`
    - `Task<BonusWorkoutEntity> LogBonusAsync(string? workoutId, string title, DateOnly date, int? durationMinutes)`; `Task<IReadOnlyList<BonusWorkoutEntity>> GetBonusWorkoutsAsync(DateOnly from, DateOnly to)`; `Task RemoveBonusAsync(Guid id)`
  - `SqliteTrackingRepository(ShowUpDatabase db, TimeProvider time, IUserScope user, IDeviceIdProvider device) : ITrackingRepository` — every write sets `UserId`, `DeviceId`, `CreatedAt/UpdatedAt` from `time.GetUtcNow()`; every read filters `DeletedAt == null` and `UserId == user.CurrentUserId` (null matches null). "Today" = `DateOnly.FromDateTime(time.GetLocalNow().DateTime)`.

- [ ] **Step 1: Failing tests** (each test uses a fresh temp-file db, deleted in cleanup):

```csharp
[TestMethod] public async Task Migrations_FreshDb_SetsUserVersion1()
[TestMethod] public async Task CrossSession_FutureDay_Throws()
[TestMethod] public async Task CrossSession_PastDay_IsAllowed()
[TestMethod] public async Task CrossSession_Twice_StoresOneRow()
[TestMethod] public async Task GetCompleted_DuplicateRowsFromTwoDevices_CountsOnce()   // insert 2 rows directly
[TestMethod] public async Task Uncross_DuplicateRows_TombstonesAll()
[TestMethod] public async Task Reads_OtherUsersRows_AreInvisible()                      // FakeUserScope "a" vs "b"
[TestMethod] public async Task EndEnrollment_RemovesFromActive()
[TestMethod] public async Task Bonus_LogAndRangeQuery_RoundTrips()
[TestMethod] public async Task Writes_SetUpdatedAtAndDeviceId()
```

- [ ] **Step 2:** FAIL. **Step 3:** Implement. **Step 4:** PASS. **Step 5:** Register `ShowUpDatabase` (path = `Path.Combine(ApplicationData.Current.LocalFolder.Path, "showup.db")` in the head), `ITrackingRepository`, `TimeProvider.System`, anonymous `IUserScope`, and a `IDeviceIdProvider` that persists a GUID in preferences. **Step 6:** Commit `feat(core): sqlite storage and tracking repository`, PR.

### Task 7: SQLite on WASM spike (persistence across reloads)

**Files:**
- Create: `docs/storage.md`
- Modify (temporary, behind `#if DEBUG`): `src/ShowUp/Views/MainView.xaml(.cs)` — a debug text showing `Bonus rows: N` and a "Add row" button using `ITrackingRepository.LogBonusAsync`

**Interfaces:**
- Consumes: Task 6 registrations.
- Produces: verified answer "does sqlite-net persist on browserwasm across reload?" recorded in `docs/storage.md`.

- [ ] **Step 1:** Run the WASM head (`dotnet run -f <browserwasm TFM>` or Uno-app MCP `uno_app_start`), open in Playwright, click "Add row" twice, reload page.
- [ ] **Step 2:** Expected: count shows 2 after reload. Screenshot to `.screenshots/wasm-sqlite.png`.
- [ ] **Step 3:** If it does not persist: try (a) Uno.Wasm.Bootstrap IDBFS (`<WasmShellEnableIDBFS>true</WasmShellEnableIDBFS>`) with an explicit flush/sync after writes, then (b) the fallback from the spec: a `JsonTrackingRepository : ITrackingRepository` for browserwasm only, passing the same Task 6 test suite (make the suite an abstract base class with two concrete subclasses). Record the finding (Uno issue if it's a platform gap).
- [ ] **Step 4:** Same check on Desktop and Windows heads (count survives app restart).
- [ ] **Step 5:** Remove the debug UI or keep it behind `#if DEBUG` in a dev-only settings section; write `docs/storage.md` (what's stored where per head, migrations, the WASM result). Commit `docs: storage per platform`, PR.

---

## Following plans (written when this one is done)

2. Shell & theme (responsive nav, paper/chalkboard theme, fonts, page skeletons)
3. Catalog research & verification (parallel per program family) → `catalog/programs/*.json` + hero art
4. Calendar + `MarkerCrossControl` (strokes, sound, haptics) + Today page + bonus workouts UI
5. Programs browse/detail + enroll flow
6. Entitlements (Free/Pro, Uno.RevenueCat)
7. Reminders per platform
8. Custom schedule builder
9. Supabase schema + auth + sync + catalog publish job
10. Stats/history, celebrations, accessibility, store metadata
