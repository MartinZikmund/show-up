# Accounts

Signing in is optional. The app works fully signed out; an account is what cross-device sync (Pro) will hang off.
Sign-in is an emailed 6-digit code through Supabase Auth (see [supabase.md](./supabase.md) for the client and where
tokens live).

## The account card

Settings → Account, hidden when the build has no Supabase project (`SupabaseOptions.IsConfigured` is false):

- **Signed out:** email → **Send code** → the 6-digit code → **Sign in**. A pasted `123 456` or "Your code: 123456"
  still counts (`AccountViewModel.NormalizeCode`). Enter submits either box. Errors are localized: not an email, wrong
  or expired code, rate limited, can't reach the server.
- **Signed in:** the email, **Sign out**, and a "Sync · Pro feature, coming soon" row until the sync engine lands.

The card is `AccountViewModel` (Core), exposed as `SettingsViewModel.Account`. The flow behind it is `AccountService`.

## Whose rows you see

Every syncable row carries `UserId`: null for rows made signed out, the account's id otherwise. The repositories read
and stamp through `IUserScope`, whose live implementation is `UserScope`. Only `AccountService` switches it, and every
switch runs in this order:

1. `UserScope.Set(userId)`: the repositories now see that account's rows only.
2. `IEntitlementService.SetUserAsync(userId)`: the store's user and the per-account Pro cache follow.
3. `DataChangeAnnouncer.Announce()`: drops the enrollment and catalog caches, then sends one `DataChangedMessage`.
   Pages reload, `SyncedPersonalization` re-reads the marker color, cross-off style and units, and `ReminderCoordinator`
   re-plans for the account.

At startup `AccountService.RestoreAsync` runs before anything loads, so the first page already reads the right rows
(steps 1 and 2 only: nothing to announce yet).

A session the server revokes (a dead refresh token) raises `SessionChanged`; `AccountService` switches back to
signed out on the UI thread.

## Bringing local progress in

The first time an account signs in on a device that has signed-out progress, the app asks "Bring your local progress
into this account?".

- **Yes:** `ILocalDataClaim.ClaimAsync` gives every live signed-out row of every synced table to the account in one
  transaction, bumping `UpdatedAt` (whole microseconds, always after the row's own stamp) and `DeviceId` so the next
  push sends them.
- **No:** the rows stay signed-out rows. They are hidden while signed in and come back after signing out.

Settings rows alone (every install has some) don't count as progress, so a fresh install never asks. The answer is
remembered per account per device (preference `AccountClaimAsked.{hash}`), so the same account is never asked twice.
If the claim fails, the sign-in is undone and the next attempt asks again.

Accounts never mix: rows claimed by Alice keep her id, so Bob signing in on the same device sees none of them, is not
asked about them, and can never upload them.

## Signing out

Nothing local is deleted. If `ISyncStatus.HasUnsyncedChangesAsync()` says some changes haven't reached the account,
the app warns first. Until the sync engine lands `NoSyncStatus` answers false (nothing is ever deleted, so nothing is
lost). After sign-out the scope is signed out again and the signed-out rows are back.

## Trying it without a Supabase project

Debug builds have **Settings → Debugging → Simulate sign-in**. It swaps in `DebugSupabaseAuth`, a fake account server:
any email, code `123456`; the same email is always the same account. Emails starting with `ratelimit` or `offline`
fake those failures. The simulated session survives a restart, so restoring at startup can be tried too. Switching it
signs out first.
