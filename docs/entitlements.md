# Show Up Pro: entitlements and paywall

How the app decides who is Pro, what each head does about it, and what to configure before shipping.

## The pieces

- `FeatureGate` (Core) answers "may a Free user do this?" and holds the limits: 1 active program, 1 reminder (20 for Pro).
  Marker color falls back to Auto while not Pro; the stored choice is kept for when Pro returns.
- `IEntitlementService` (Core) is the one contract. `IsPro` is live: listen to `Changed`.
- `ProViewModel` / `ProView` is the paywall. Settings has a "Show Up Pro" card, and `ProGateHint` is the small
  "Unlock with Pro" link to put under anything a Free user cannot use (bind it to a page's `OpenProCommand`).
- `PurchaseResolver` decides the outcome of a purchase. A purchase counts only once Pro actually shows up;
  otherwise the user is told to check back or tap Restore. `SingleFlight` stops overlapping refreshes.

## Per head

| Head | Service | Pro when |
| --- | --- | --- |
| Android, iOS | `RevenueCatEntitlementService` | the `pro` entitlement is active; cached offline until its expiry |
| Windows (packaged WinAppSDK) | `MicrosoftStoreEntitlementService` | an active Store add-on license matches a configured Store ID |
| Desktop Skia, WebAssembly | `UnlockedEntitlementService` | always |

A store head with no key or Store IDs is Free and the paywall says "Purchases aren't available in this build".
If the store should answer but does not (offline), it says "Couldn't load the offers" with a retry button.

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

## Later

Entitlements are per store until Plan 4 (Supabase accounts). Then RevenueCat should `LoginAsync` with the account id
so a purchase follows the user across devices; today it uses an anonymous RevenueCat user.
