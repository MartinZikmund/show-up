| T1 → T4/T6 | schema/tables/RLS | must mirror local tables v1–v4 (incl. ink_json, notes, metrics, subject_key, reminders, custom programs, synced settings) — T1 brief lists v1–v3 only; Ruling: mirror ALL local syncable tables through v4 |
Ruling: T1 mirrors every syncable local table through storage v4 — cost if wrong: none.
Ruling: Windows packaged uses DPAPI file token store (no Credential Locker: not isolated for full-trust, roams → GoTrue reuse revocation) — cost if wrong: none
Ruling: claim prompt re-offers whenever anonymous rows exist at sign-in (with count), not only once per account — cost if wrong: an extra prompt
Ruling: FeatureGate.IsAvailable(CloudSync) = SupabaseOptions.IsConfigured (not a hard flag) — cost if wrong: paywall shows sync before a hosted project, only when configured
Ruling: OTA downloads catalog_workouts/programs with version<=N as the full set → Task 6 publish MUST write the full workout set each version (bump version on every row) — cost if wrong: stale workouts
Ruling: missing pinned revision falls back to latest revision of the program (programs never removed) instead of a 'details unavailable' screen — cost if wrong: an enrollment shows newer days
Ruling: allowed remote hero that fails to load → accent gradient (single heroImage field, no second source) — cost if wrong: plain gradient on failed load
Ruling: ms-appx: hero paths refused (only plain relative bundled paths) — cost if wrong: none shipped uses it
Final review: Needs fix wave (partial-publish orphan version; Confirm signup template needs {{ .Token }}; SetStoreUserAsync try/finally) + minors. Ruling: server-side Pro enforcement (RLS ignores entitlements) deferred to a later plan with RevenueCat webhooks — cost if wrong: modified clients can sync without Pro
Ruling: add server column size CHECKs now in the fix wave (ink_json 64 KB, program_json 256 KB, notes text 16 KB, text_value 4 KB) — cost if wrong: a legit oversize row rejected (client limits are smaller)
Ruling: Settings "session ended" InfoBar (x:Bind OneWay IsOpen + IsClosable) may not reopen on a second revocation in one run on Uno heads — parked unverified, low confidence — cost if wrong: the second notice in one app run is missed; the account card still shows signed-out.
Ruling: residual races left as is (Paused can be replaced by Offline/Failed until the next retry; ResolvePinnedAsync can re-add a pinned revision right after Invalidate) — narrow windows, self-healing on the next pass — cost if wrong: a briefly wrong sync label, or one stale pinned custom revision until the next invalidation.
Ruling: custom program JSON client limit equals the server CHECK (262144 bytes, both inclusive) rather than sitting below it — never looser, so accepted — cost if wrong: none.
Ruling: builder "too big to save" message has no view-model test — accepted, the validator rule is tested — cost if wrong: a regression in the message wiring goes unnoticed.
Ruling: handoff doc says "pushed", fix report says "not pushed" — not worth a CI cycle on the fix PR; corrected when the handoff docs are refreshed after the merge — cost if wrong: none.
