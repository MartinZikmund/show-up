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

- [calendar.md](./calendar.md) — the program calendar page: the active enrollment service, tap-to-cross flow, and the reusable grid control.
- [catalog.md](./catalog.md) — how the bundled workout programs are verified, the file format, optional sessions, and held programs.
- [json-aot-serialization.md](./json-aot-serialization.md) — `JsonSerializerContext` conventions for AOT-safe, trim-safe JSON serialization.
- [marking.md](./marking.md) — the hand-drawn marker X, drawing the cross yourself, sound and haptics, and the cross moment.
- [programs.md](./programs.md) — browsing the catalog, the program detail page, starting a program, and the hero fallback.
- [storage.md](./storage.md) — where the SQLite database lives per head, migrations, and the WebAssembly persistence fix.
- [theme.md](./theme.md) — marker color picker, how it repaints live, and the grain paper background.
- [today.md](./today.md) — the Today page, the shared crossing flow, and bonus workouts.
- [views.md](./views.md) — `ViewBase<TViewModel>`, `IViewBase`, and how views resolve their view models.

## Release

- [deployment.md](./deployment.md) — hosting the WebAssembly head on Cloudflare Workers: production, PR previews, one-time setup.
- [release-pipeline.md](./release-pipeline.md) — what each workflow builds and publishes, environments, secrets, fork behaviour.
- [release-runbook.md](./release-runbook.md) — cutting a release, shipping a patch, halting a bad rollout.
- [versioning.md](./versioning.md) — the Dev/Prod channel model, git-height versions, side-by-side identity.
- [windows-packaging.md](./windows-packaging.md) — the MSIX path, Store identity, and Microsoft Store submission.

