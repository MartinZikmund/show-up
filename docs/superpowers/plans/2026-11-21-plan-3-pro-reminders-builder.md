# Show Up — Plan 3: Pro, Reminders and Custom Schedules Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Free/Pro tiers with real store billing on Android/iOS (everything unlocked elsewhere), multiple concurrent programs for Pro, workout reminders on every head, a custom schedule builder, and a basic History page.

**Architecture:** Feature gating is a pure Core function over an `IEntitlementService` whose implementation is chosen per head (RevenueCat on Android/iOS, always-Pro elsewhere). Reminders and custom programs are new sync-ready SQLite tables (migration **v4**, frozen DDL — v2 = InkJson, v3 = notes/metrics with SubjectKey). Custom programs are ordinary `WorkoutProgram`s stored as JSON and merged into `ICatalogService`. Reminders are planned in Core and delivered by a per-head `IReminderScheduler`.

**Tech Stack:** Uno.Sdk 7.1 dev, `MZikmund.Uno.RevenueCat` (Android/iOS only), WinAppSDK `AppNotificationManager`, Android `AlarmManager` + notifications, iOS `UNUserNotificationCenter`, browser Notification API via JS interop.

**Spec:** `docs/superpowers/specs/2026-11-21-show-up-design.md` (Product, Reminders, Lapse/downgrade).

## Global Constraints

- All Plan 1 and Plan 2 global constraints apply (`.superpowers/sdd/2026-11-21-plan-2-core-experience/global-constraints.md` content copied into this plan's workspace).
- **Free tier (spec):** full catalog browsing, **one active program**, bonus workouts, **one reminder**, local tracking + history of the current program. **Pro:** cloud sync, custom schedule builder, multiple concurrent programs, multiple reminders, full history/stats, extra themes and marker colors.
- **Billing:** Android + iOS via `MZikmund.Uno.RevenueCat`, one entitlement id `pro`, packages monthly / yearly / lifetime from the current offering, API keys from configuration `RevenueCat:AndroidApiKey` / `RevenueCat:AppleApiKey` (empty by default; CI secrets later). **Windows (WinAppSDK, packaged)** via Microsoft Store `Windows.Services.Store.StoreContext`: subscription add-on (monthly/yearly) + durable lifetime add-on, Store IDs from configuration `MicrosoftStore:ProMonthlyStoreId` / `ProYearlyStoreId` / `ProLifetimeStoreId`; `InitializeWithWindow` with the main window HWND before purchase UI; refresh on `OfflineLicensesChanged`. **Desktop Skia (macOS/Linux/unpackaged Windows) and WASM → Pro.** A store head without keys/IDs or without a Store license → Free with the paywall showing "Purchases aren't available in this build". Store entitlements are per-store until Supabase account entitlements (Plan 4).
- **Lapse (spec):** nothing deleted. Extra active programs become read-only except the one the user picks as primary; custom programs read-only (still visible, can't edit or start); extra reminders disabled but kept.
- Migration v4 is **frozen explicit DDL** like v1–v3 (each in a transaction); all new tables carry the sync columns (`Id`, `UserId`, `DeviceId`, `CreatedAt`, `UpdatedAt`, `DeletedAt`).
- Notifications text localized (en + cs), names the day's workout: "Plyometric Cardio Circuit is waiting. Show up." / rest day: "Rest day. You earned it." — no reminder fires for a day already fully crossed.

- Existing hooks from Plan 2 to wire: `SettingsViewModel.CanChangeMarkerColor` → `FeatureGate.CanUse(Personalization)`; Results charts are later (#22); all crossing goes through `ICrossingCoordinator`/`IActiveEnrollmentService`; syncable rows carry a stable `SubjectKey` where they have a natural key (follow `NoteEntity`/`MetricEntryEntity`).
- Today page may later merge into Calendar (#58) — multi-program UI must work in `TodayPanel` and `ProgramCalendarControl` both.

## Review Focus

1. **Free user with 2 active enrollments after lapse** — app must not crash or silently end one; it asks which stays primary; the other is read-only. Test in Task 2.
2. **Reminder at a time that already passed today** — schedules for the next matching day, not immediately. Test in Task 4.
3. **DST change week** — reminder stays at the same local wall-clock time. Test in Task 4 (FakeTimeProvider with a zone that has DST).
4. **Custom program with zero sessions on a day / 0 weeks** — builder blocks save with a clear message; validator rules apply. Test in Task 6.
5. **Duplicate purchase taps / cancelled purchase** — no double purchase, cancelled leaves state unchanged and shows no error. Test in Task 1 (fake billing).

---

### Task 1: Entitlements and paywall

**Files:** Core `Entitlements/ProFeature.cs`, `FeatureGate.cs`, `IEntitlementService.cs`, `ProOffer.cs`, `ViewModels/ProViewModel.cs`; head `Services/Entitlements/UnlockedEntitlementService.cs`, `RevenueCatEntitlementService.cs` (`#if __ANDROID__ || __IOS__`), `MicrosoftStoreEntitlementService.cs` (WinAppSDK only, `#if WINDOWS` / `!HAS_UNO`), `Views/ProView.xaml(.cs)`; csproj: `MZikmund.Uno.RevenueCat` referenced only for android/ios TFMs (CPM version `0.3.0-beta.4` or newest); tests `Entitlements/FeatureGateTests.cs`, `ViewModels/ProViewModelTests.cs`.

**Interfaces:**
- `enum ProFeature { CloudSync, CustomSchedules, MultiplePrograms, MultipleReminders, FullHistory, Personalization }`
- `static class FeatureGate { bool CanUse(ProFeature feature, bool isPro); int MaxActivePrograms(bool isPro) /* 1 or int.MaxValue */; int MaxReminders(bool isPro) /* 1 or 20 */; }`
- `interface IEntitlementService { bool IsPro { get; } bool PurchasesAvailable { get; } event EventHandler? Changed; Task RefreshAsync(); Task<IReadOnlyList<ProOffer>> GetOffersAsync(); Task<PurchaseOutcome> PurchaseAsync(ProOffer offer); Task<bool> RestoreAsync(); }`; `sealed record ProOffer(string Id, string Title, string Price, ProOfferKind Kind)`; `enum ProOfferKind { Monthly, Yearly, Lifetime }`; `enum PurchaseOutcome { Purchased, Cancelled, Pending, Failed, Unavailable }`.
- RevenueCat impl caches last known `IsPro` in `IAppPreferences` so offline launches keep Pro; refresh on start and resume.
- `ProViewModel`: feature list (localized), offers, `PurchaseCommand` (disabled while in flight), `RestoreCommand`, state text.
- Settings gets a "Show Up Pro" card (status + open paywall). Navigation: Pro page reachable from Settings and from every gate prompt.
- Microsoft Store mapping is a pure Core helper `StoreLicenseMapper.IsPro(IEnumerable<(string StoreId, bool IsActive, DateTimeOffset? Expires)> addOns, DateTimeOffset now, IReadOnlySet<string> proStoreIds)` so it's unit-testable.
- [ ] Tests: `StoreLicense_ActiveSubscription_IsPro`, `StoreLicense_ExpiredSubscription_IsFree`, `StoreLicense_Lifetime_IsPro`, `CanUse_FreeCustomSchedules_IsFalse`, `MaxActivePrograms_Free_IsOne`, `Purchase_DoubleTap_PurchasesOnce`, `Purchase_Cancelled_KeepsFreeWithoutError`, `Offers_Unavailable_ShowsMessage`.
- [ ] Build android + ios + windows TFMs (CI covers otherwise); Windows: verify the paywall shows "unavailable" gracefully on an unlicensed dev build. Commit, PR `Closes #13`.

### Task 2: Multiple active programs (Pro) and lapse handling

**Files:** Core `Tracking/IActiveEnrollmentService.cs` (extend), `ActiveEnrollmentService.cs`, `ViewModels/ProgramDetailViewModel.cs`, `TodayViewModel.cs`, `ProgramCalendarViewModel.cs`; head program switcher control in Today/Calendar headers; Storage: `enrollments` gets nothing new — primary choice stored in `synced_settings`? → add `IsPrimary` via `synced_settings` key `primaryEnrollmentId`.

**Interfaces (Plan 2 final-review ruling):** every write/query API on `IActiveEnrollmentService`, `ICrossingCoordinator` (`CrossTarget` gains `Guid EnrollmentId`) and `IJournalService` takes an explicit `Guid enrollmentId`; the UI keeps a *selected enrollment* (persisted in synced settings). Add a Core query `IScheduleQuery.GetDueAsync(DateOnly date)` → sessions due across all active enrollments (used by Today and reminders).
- `IActiveEnrollmentService`: `Task<IReadOnlyList<ActiveEnrollment>> GetAllAsync()`, `Guid? SelectedEnrollmentId` + `Task SelectAsync(Guid)` (which one Today/Calendar shows), `bool IsReadOnly(ActiveEnrollment)` (lapsed and not primary), `Task SetPrimaryAsync(Guid)`.
- Start flow: Free with an active program → current Plan 2 behavior (end current, confirm) plus an "Unlock Pro to run several programs" link; Pro → starts alongside.
- Today with several programs: sessions grouped per program; Calendar: switcher (ComboBox/SelectorBar) in header.
- Lapse: when `IsPro` flips false with >1 active, a one-time dialog asks which stays primary; others render read-only (no crossing, banner explains with Pro link).
- [ ] Tests: `Start_ProWithActive_StartsAlongside`, `Start_FreeWithActive_EndsAfterConfirm`, `Lapse_TwoActive_OnlyPrimaryCrossable`, `Today_TwoPrograms_GroupsSessions`. Commit, PR.

### Task 3: Storage v4 — reminders, custom programs and synced settings tables

**Files:** Core `Storage/Migrations.cs` (v4 frozen DDL), `ReminderEntity.cs`, `CustomProgramEntity.cs`, `SyncedSettingEntity.cs`, `IReminderRepository.cs` + sqlite impl, `ICustomProgramRepository.cs` + sqlite impl, `ISyncedSettings.cs` + impl; tests.

**Interfaces:**
- `ReminderEntity : SyncableEntity { int MinutesOfDay; int DaysMask /* bit 0 = Monday */; bool Enabled; }`
- `CustomProgramEntity : SyncableEntity { string ProgramJson; int Revision; }`
- `SyncedSettingEntity : SyncableEntity { string Key; string Value; }` (unique by Key per user — upsert)
- Repos: CRUD with tombstones, current-user scoping (same patterns as `SqliteTrackingRepository`).
- [ ] Tests: `Migrate_V3Db_ToV4_KeepsData`, `Migrate_Fresh_IsV4`, reminder/custom/settings round-trips, tombstones, user scoping. Commit, PR.

### Task 4: Reminder planning (Core) and scheduler abstraction

**Files:** Core `Reminders/ReminderPlanner.cs`, `IReminderScheduler.cs`, `ReminderMessageBuilder.cs`, `ViewModels/RemindersViewModel.cs`; head `Views/RemindersView.xaml` (from Settings), tests.

**Interfaces:**
- `sealed record PlannedReminder(Guid ReminderId, DateTimeOffset FireAt, string Title, string Body)`
- `static class ReminderPlanner { IReadOnlyList<PlannedReminder> Plan(IReadOnlyList<ReminderEntity> reminders, DateTimeOffset nowLocal, TimeZoneInfo zone, int days, Func<DateOnly, ReminderDay> dayInfo); }` — next `days` (14) occurrences per enabled reminder, skipping days already fully crossed (`ReminderDay(bool Completed, string? WorkoutTitle, bool IsRest)`), wall-clock time honored across DST.
- `interface IReminderScheduler { bool IsSupported { get; } Task<bool> RequestPermissionAsync(); Task ReplaceAllAsync(IReadOnlyList<PlannedReminder> reminders); }` — re-planned on app start, resume, any tracking change, reminder edit.
- RemindersViewModel: list, add (TimePicker + weekday toggles), enable/disable, delete; Free limited to 1 (gate prompt beyond).
- [ ] Tests: `Plan_TimePassedToday_StartsTomorrow`, `Plan_DstSpringForward_KeepsWallClock`, `Plan_DayCompleted_Skips`, `Plan_RestDay_RestMessage`, `Free_SecondReminder_ShowsGate`. Commit, PR.

### Task 5: Platform reminder schedulers

**Files:** head `Platforms/Android/Reminders/*` (AlarmManager exact/inexact with `SCHEDULE_EXACT_ALARM` fallback, `BroadcastReceiver` posting a notification on channel "reminders", BOOT_COMPLETED re-plan, POST_NOTIFICATIONS permission on 33+), `Platforms/iOS/Reminders/*` (UNUserNotificationCenter, max 64 pending — 14 days fits), Windows (`Microsoft.Windows.AppNotifications` builder + `ToastNotificationManager` scheduled toasts for packaged app; if scheduled toasts unavailable in WinAppSDK, keep in-app timer while running and document), WASM (`Notification` + `setTimeout` via `[JSImport]`, only while a tab is open), Desktop Skia (in-app `InfoBar`/`TeachingTip` + sound while running). Docs `docs/reminders.md` with a per-head table of what works.
- [ ] Verify on Desktop, WASM (Playwright: grant notification permission), Windows; Android/iOS: build + emulator if available, else document as build-verified only. Record Uno gaps in uno-issues. Commit, PR `Closes #14`.

### Task 6: Custom schedule builder (Pro)

**Files:** Core `Catalog/VerificationLevel.cs` (add `Custom`), `CustomPrograms/CustomProgramFactory.cs`, `ViewModels/ScheduleBuilderViewModel.cs`; head `Views/ScheduleBuilderView.xaml(.cs)`; `CatalogService` merges custom programs from `ICustomProgramRepository` (ids `custom-<guid>`), Programs page gets a "My schedules" section + "New schedule" button (Pro gate), tests.

**Interfaces:**
- Builder: name, accent color (palette of 8), weeks (1–26), per day a list of sessions picked from catalog workouts (search, same as bonus dialog) or a custom workout (title + minutes + kind → stored inline as workouts with ids `custom-<guid>-<n>` inside the program JSON — extend program JSON with an optional `workouts` array used only by custom programs; validator accepts it), rest-day toggle, copy week, optional sessions.
- Saves as a new `Revision` on edit (enrollments stay pinned).
- [ ] Tests: `Save_EmptyDay_Blocked`, `Save_ZeroWeeks_Blocked`, `Edit_BumpsRevision_EnrollmentKeepsOld`, `CatalogService_IncludesCustomPrograms`, `Free_OpenBuilder_ShowsGate`. Commit, PR `Closes #15`.

### Task 7: History page (basic)

**Files:** Core `ViewModels/HistoryViewModel.cs`, head `Views/HistoryView.xaml`; tests.
- Lists enrollments (current first, then ended with finished/abandoned), per program: days crossed / total, longest streak, start–end dates; bonus workouts by month. Free: current program + last 30 days of bonus workouts, the rest behind a Pro gate row ("Unlock full history").
- [ ] Tests: `Free_ShowsOnlyCurrentProgram`, `Pro_ShowsEndedPrograms`, `Bonus_GroupsByMonth`. Commit, PR (refs #22 Statistics).

## Following plans

4. Supabase: schema/RLS/auth/sync + catalog publishing + OTA catalog
5. Hero art, celebrations, accessibility pass, store metadata, release
