# Accounts

Signing in is optional. The app works fully signed out; an account is what cross-device sync (Pro) will hang off.
Sign-in is an emailed 6-digit code through Supabase Auth (see [supabase.md](./supabase.md) for the client and where
tokens live).

## The account card

Settings → Account, hidden when the build has no Supabase project (`SupabaseOptions.IsConfigured` is false):

- **Signed out:** email → **Send code** → the 6-digit code → **Sign in**. A pasted `123 456` or "Your code: 123456"
  still counts (`AccountViewModel.NormalizeCode`). Enter submits either box; the boxes turn read-only (not disabled,
  so focus stays) while a call runs. "Send a new code" waits 30 s after each code. Errors are localized (not an
  email, wrong or expired code, rate limited, can't reach the server) and announced to screen readers, also when one
  error replaces another.
- **Signed in:** the email, **Sign out**, and a "Sync · Pro feature, coming soon" row until the sync engine lands.

The card is `AccountViewModel` (Core), exposed as `SettingsViewModel.Account`. The flow behind it is `AccountService`.

## Whose rows you see

Every syncable row carries `UserId`: null for rows made signed out, the account's id otherwise. The repositories read
and stamp through `IUserScope`, whose live implementation is `UserScope`. Only `AccountService` switches it, and every
switch runs in this order:

1. Pending personalization writes are flushed, so they land in the account that made them.
2. `UserScope.Set(userId)` and, in the same breath, `DataChangeAnnouncer.InvalidateCaches()`: from here on the
   repositories and the enrollment and catalog caches only know that account's rows, so a cross or a note saved
   meanwhile can't reach the previous account's.
3. `IEntitlementService.SetUserAsync(userId)`: the store's user and the per-account Pro cache follow. Bounded to 10 s
   (`AccountService.StoreUserTimeout`; RevenueCat's login/logout has its own 10 s bound) so a store that never
   answers can't hold the switch.
4. `DataChangeAnnouncer.Announce()`: one `DataChangedMessage`. Pages reload (Today, Calendar, History, Reminders,
   program detail), `SyncedPersonalization` re-reads the marker color, cross-off style and units (a key the new
   account has no value for falls back to the default, never the previous account's), and `ReminderCoordinator`
   re-plans for the account. Open notes and results dialogs close unsaved.

The program Calendar shows (`SelectedEnrollmentId`) is remembered per account on each device.

At startup `AccountService.RestoreAsync` runs before anything loads, so the first page already reads the right rows
(scope and store user only: nothing to announce yet).

A session the server revokes (a dead refresh token) raises `SessionChanged`; `AccountService` switches back to
signed out on the UI thread. One that arrives during a sign-in or sign-out is applied as soon as that finishes.

## Bringing local progress in

Whenever a sign-in finds signed-out progress on the device, the app offers it, with a count:
"Bring 2 programs, 23 crossed workouts and 4 other entries into this account?" (programs are enrollments, crossed
workouts are completions, other entries are bonus workouts, notes, results, reminders, schedule shifts and custom
programs; settings never count, every install has some).

- **Yes:** `ILocalDataClaim.ClaimAsync` gives every live signed-out row of every synced table to the account in one
  transaction and sets `DeviceId`. Rows get `UpdatedAt` bumped (whole microseconds, always after the row's own stamp)
  so the next push sends them; **settings keep their own `UpdatedAt`**, so the account's settings from other devices,
  being newer, win.
- **No:** the rows stay signed-out rows. They are hidden while signed in, come back after signing out, and are
  offered again at the next sign-in.

If the claim fails, the sign-in is undone and the claim's error is the one reported.

Accounts never mix: rows claimed by Alice keep her id, so Bob signing in on the same device sees none of them, is not
offered them, and can never upload them.

## Signing out

Nothing local is deleted. If `ISyncStatus.HasUnsyncedChangesAsync()` says some changes haven't reached the account,
the app warns first. Until the sync engine lands `NoSyncStatus` answers false (nothing is ever deleted, so nothing is
lost). After sign-out the scope is signed out again and the signed-out rows are back.

## Trying it without a Supabase project

Debug builds have **Settings → Debugging → Simulate sign-in**. It swaps in `DebugSupabaseAuth`, a fake account server:
any email, code `123456`; the same email is always the same account. Emails starting with `ratelimit` or `offline`
fake those failures. The simulated session survives a restart, so restoring at startup can be tried too. Switching it
signs out first.
