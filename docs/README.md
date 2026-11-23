# Docs

One page per topic. Keep it that way.

## Building & tooling

- [building.md](./building.md) — target frameworks, per-platform prerequisites, build and run commands.
- [community-toolkit-copy.md](./community-toolkit-copy.md) — the temporary source copy of the CommunityToolkit pieces the app uses, until the toolkit supports Uno 7.
- [dev-assets.md](./dev-assets.md) — the Dev-channel icon and splash, badged at build time from your prod artwork.
- [template-upgrades.md](./template-upgrades.md) — bringing later template improvements into an app created from it.
- [worktree-identity.md](./worktree-identity.md) — per-worktree package identity, so two git worktrees can be installed and run side by side.
- [xaml-styler.md](./xaml-styler.md) — XAML formatting rules and how CI enforces them.

## Architecture

- [accounts.md](./accounts.md) — optional email-code sign-in, the Settings account card, user scoping, claiming signed-out progress and the simulated sign-in for debug builds.
- [calendar.md](./calendar.md) — the program calendar page: the enrollment service, tap-to-cross flow, and the reusable grid control.
- [catalog.md](./catalog.md) — how the bundled workout programs are verified, the file format, optional sessions, and held programs.
- [catalog-delivery.md](./catalog-delivery.md) — over-the-air catalog updates: the check, validation, the local cache, pinned revisions and remote hero images.
- [custom-schedules.md](./custom-schedules.md) — the Pro schedule builder: drafts, own workouts, revisions, the catalog merge and read-only after a lapse.
- [entitlements.md](./entitlements.md) — Show Up Pro: the feature gate, the paywall, per-head billing (RevenueCat, Microsoft Store, unlocked) and its configuration.
- [history.md](./history.md) — the History tab: programs with streaks, bonus workouts by month, and the Free limit.
- [json-aot-serialization.md](./json-aot-serialization.md) — `JsonSerializerContext` conventions for AOT-safe, trim-safe JSON serialization.
- [marking.md](./marking.md) — the hand-drawn marker X, drawing the cross yourself, sound and haptics, and the cross moment.
- [multiple-programs.md](./multiple-programs.md) — running several programs at once (Pro): enrollment-addressed APIs, the selected program, Today's groups, and read-only programs after a lapse.
- [programs.md](./programs.md) — browsing the catalog, the program detail page, starting a program, and the hero fallback.
- [reminders.md](./reminders.md) — planning notifications for the next 14 days, delivery on each head, and the Free limit after a lapse.
- [results.md](./results.md) — notes, logging Fit Test and Max Out results, the day detail sheet and the results table.
- [statistics.md](./statistics.md) — workouts per week and by kind, the consistency heatmap, program comparison, and why bars count workouts rather than minutes.
- [storage.md](./storage.md) — where the SQLite database lives per head, migrations, and the WebAssembly persistence fix.
- [supabase.md](./supabase.md) — the Supabase schema mirroring local storage, RLS and grants, pgTAP tests, the CI job, and the local↔server column mapping, and the thin client (config keys, where tokens live per head).
- [sync-classification.md](./sync-classification.md) — which settings and rows follow the user across devices and which stay on this one.
- [sync.md](./sync.md) — the sync engine: dirty marker, push and pull, cursors and overlap, last writer wins, triggers, states and the sign-out prompt.
- [theme.md](./theme.md) — marker color picker, how it repaints live, and the grain paper background.
- [today.md](./today.md) — the Today page, the shared crossing flow, and bonus workouts.
- [views.md](./views.md) — `ViewBase<TViewModel>`, `IViewBase`, and how views resolve their view models.

## Release

- [deployment.md](./deployment.md) — hosting the WebAssembly head on Cloudflare Workers: production, PR previews, one-time setup.
- [release-pipeline.md](./release-pipeline.md) — what each workflow builds and publishes, environments, secrets, fork behaviour.
- [release-runbook.md](./release-runbook.md) — cutting a release, shipping a patch, halting a bad rollout.
- [versioning.md](./versioning.md) — the Dev/Prod channel model, git-height versions, side-by-side identity.
- [windows-packaging.md](./windows-packaging.md) — the MSIX path, Store identity, and Microsoft Store submission.

