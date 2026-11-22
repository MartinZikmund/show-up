# Show Up Pro: entitlements and paywall

How the app decides who is Pro, what each head does about it, and what to configure before shipping.

## The pieces

- `FeatureGate` (Core) answers "may a Free user do this?" and holds the limits: 1 active program, 1 reminder (20 for Pro).
  Marker color falls back to Auto while not Pro (the Settings picker shows Auto too); the stored choice is kept for when
  Pro returns. Several programs at once: see [multiple-programs.md](./multiple-programs.md).
- `IEntitlementService` (Core) is the one contract. `IsPro` is live: listen to `Changed`.
- `ProViewModel` / `ProView` is the paywall. Settings has a "Show Up Pro" card, and `ProGateHint` is the small
  "Unlock with Pro" link to put under anything a Free user cannot use (bind it to a page's `OpenProCommand`; `Text`
  overrides the line, e.g. "Unlock Pro to run several programs").
- `PurchaseResolver` decides the outcome of a purchase. A purchase counts only once Pro actually shows up;
  otherwise the user is told to check back or tap Restore. A network or server error after checkout is `Unconfirmed`
  ("we couldn't confirm your purchase"). `SingleFlight` stops overlapping refreshes; a refresh asked for while one
  runs gets one trailing run, so it never returns state from before a purchase.
- `ExpiryWatch` rechecks the store when a cached subscription reaches its end mid-session (RevenueCat and Store):
  a renewal keeps Pro, a real lapse (or no answer) raises `Changed`.
- The paywall re-reads the manage-subscription link after a purchase or restore; lifetime owners get none.

## Per head

| Head | Service | Pro when |
| --- | --- | --- |
| Android, iOS | `RevenueCatEntitlementService` | the `pro` entitlement is active; cached offline until its expiry |
| Windows (packaged WinAppSDK) | `MicrosoftStoreEntitlementService` | an active Store add-on license matches a configured Store ID |
| Desktop Skia, WebAssembly | `UnlockedEntitlementService` | always |

A store head with no key or Store IDs is Free and the paywall says "Purchases aren't available in this build".
If the store should answer but does not (offline), it says "Couldn't load the offers" with a retry button.

## Lapse matrix

What happens when Pro ends. Nothing is ever deleted.

| Thing | After a lapse | Comes back with Pro |
| --- | --- | --- |
| Programs beyond the primary one | stay visible, read-only (no crossing, shifting, journal edits); the lapse prompt asks which one is primary | writable again |
| Custom programs | visible, read-only, can't be started again | editable and startable |
| Reminders beyond the first | disabled but kept, and only once the store itself has confirmed the lapse (`IEntitlementService.IsConfirmed`): offline or a timed-out check changes nothing | stay off; the user switches them on |
| Reminders for read-only programs | no longer ask for their workouts | asked for again |
| Marker color | shows Auto; the stored choice is kept | the stored color |
| History | current program only | all programs and stats |
| Cloud sync | pauses: nothing syncs, nothing is deleted (builds without a Supabase project say "Coming soon", see [sync.md](./sync.md)) | resumes |

## Trying it out

Debug builds add a "Pro override" box under Settings > Debugging: **Real** (whatever the store says), **Force Pro** or
**Force Free** (simulates a confirmed lapse, even on Desktop and WebAssembly where Pro is unlocked). It wraps the real
service (`DebugEntitlementOverride`, DEBUG only) and raises `Changed` like a purchase would.

## Configuration

Set in `appsettings.json` (CI secrets later); all are empty or defaulted in the repo.

| Key | Meaning |
| --- | --- |
| `RevenueCat:AndroidApiKey`, `RevenueCat:AppleApiKey` | RevenueCat public SDK keys |
| `MicrosoftStore:ProMonthlyStoreId`, `ProYearlyStoreId`, `ProLifetimeStoreId` | Store IDs of the add-ons |
| `Legal:TermsUrl`, `Legal:PrivacyUrl` | paywall links, default to `docs/terms.md` and `docs/privacy.md` on GitHub |

RevenueCat packages are read from the current offering: `$rc_monthly`, `$rc_annual`, `$rc_lifetime`.

## Microsoft Store notes

- Store ids look like `9NXXXXXXXXXX`; license SKU ids look like `9NXXXXXXXXXX/0010`. The part before `/` is compared.
- Subscription add-ons are reported as `Durable` products. Query with `["Durable"]` and tell them apart by `StoreSku.IsSubscription`.
- A durable lifetime license reports a far-future expiry; that is treated as no expiry.
- `StoreContext` needs the window handle (`InitializeWithWindow`) before any purchase UI.
- `OfflineLicensesChanged` fires on a pool thread; the service hops to the UI thread before raising `Changed`.

## Accounts

`IEntitlementService.SetUserAsync(userId)` is what `AccountService` calls on sign-in, sign-out and restore. RevenueCat logs in or out so a
purchase follows the user across devices; the Microsoft Store and the unlocked heads ignore it. The cached Pro state is kept
per account (`AccountProCache`: preference keys carry a hash of the user id, anonymous keeps the original keys), so signing
in as someone else never inherits the previous account's Pro.
