# Generated Dev icon and splash

Replace `src/ShowUp/Assets/Icons/icon_foreground.svg` and `src/ShowUp/Assets/Splash/splash_screen.svg` with your
own artwork, and you're done. Every Dev-channel build stamps a **DEV** badge onto both, and Prod builds use them untouched.
There is no Dev artwork to draw, commit, or keep in sync.

## What you see

The badge copies the in-app `DevChannelBadge` from the title bar: a gold (`#FFB900`) pill with dark "DEV" lettering,
28% of the image tall, centred along the bottom edge.

| Where | Dev build |
|---|---|
| Windows Start menu, taskbar, Alt-Tab | bottom-centre, flush with the edge |
| Desktop window icon, WebAssembly favicon | bottom-centre, flush with the edge |
| Android launcher | lifted into the 66dp safe zone that every launcher mask keeps |
| Android 12+ splash | bottom-centre of the logo, clear of the circle the system crops the splash to |
| iOS home screen | lifted clear of the rounded corners |
| tvOS home screen | lifted inside the parallax safe zone |
| Splash on Windows, Desktop, WebAssembly, iOS | bottom-centre of the logo |

At 16–24px the word is unreadable, but the gold bar still marks the build.

Bottom-centre is deliberate. A corner badge has to be pulled deep into Android's circular masks and ends up on the
middle of the logo. Centred on the axis, a mask only ever lifts it straight up, so it looks the same everywhere.

### CI builds

A Dev build made on CI (`CI=true`, as GitHub Actions sets it, or `ContinuousIntegrationBuild=true`) reads **CI** in a
blue (`#0078D4`) pill with white lettering instead, so a staging deployment such as the WebAssembly site is never
mistaken for a local build. The in-app title-bar badge (`DevChannelBadge`) follows suit: it reads "CI" in the same blue,
through `AppEnvironment.IsCiBuild`, which the build sets whenever the icon's label is CI. Prod builds get no badge either way.

The worktree name is deliberately **not** on the icon: at taskbar sizes it would be 1–3px tall. Worktrees show their name
as text instead.
See [worktree-identity.md](./worktree-identity.md).

## How it works

[`src/DevAssets.targets`](../src/DevAssets.targets) runs `GenerateDevAssets` just before Uno.Resizetizer collects its
items. It runs on the Dev channel only, **including CI**, so Dev packages built from `main` carry the badge.

1. It takes the `UnoIcon` foreground and the `UnoSplashScreen` image exactly as Uno.Sdk created them from
   `UnoIconForegroundFile` and `UnoSplashScreenFile`.
2. `ComposeDevAsset` ([`src/DevAssets.Task.cs`](../src/DevAssets.Task.cs)) nests each SVG unchanged inside a wrapper and
   draws the badge on top. MSBuild's `RoslynCodeTaskFactory` compiles it, so no extra tool is needed.
3. The result goes to `obj/<config>/<tfm>/devassets/<content hash>/<original file name>`, and the items are pointed at it.

Three details hold this together. Each works around something Resizetizer does:

- **The content hash is in the folder name** because Resizetizer doesn't notice when a file changes at the same path. It
  records the foreground's *path*, not its timestamp. A new folder is a new path.
- **The file name is unchanged** because Resizetizer derives resource names from it (`@drawable/splash_screen`, the
  Windows manifest's splash entry). Nothing downstream sees a difference.
- **Android icons are re-rendered when the foreground changes.** Resizetizer's adaptive-icon generator only regenerates
  when the foreground *file* is newer than its output. Switching Dev → Prod in the same `obj/` would otherwise keep the
  badge. `InvalidateStaleAndroidAppIcons` records the foreground and its scale, and clears the stale output when they change.

"DEV" is drawn with outlines from Selawik Semibold, Microsoft's open-source (SIL OFL 1.1) stand-in for Segoe UI. It
therefore looks the same whichever machine builds it; the Linux and macOS CI runners don't have Segoe UI.

The badge's position comes from each image's own scale metadata, read the way Resizetizer reads it:
`AndroidForegroundScale` over `ForegroundScale`, `AndroidScale` over `Scale`, and so on. Changing
`UnoIconForegroundScale` or `UnoSplashScreenScale` moves it correctly.

## Turning it off

| You want | Do this |
|---|---|
| The prod artwork on a Dev build | `dotnet build … -p:GenerateDevAssets=false`, or set the property in the csproj |
| The other label, e.g. to preview a CI build locally | `-p:DevBadgeLabel=CI` (or `DEV`) |
| A hand-drawn Dev icon | set `GenerateDevAssets` to `false` and add `<UnoIconForegroundFile Condition="'$(AppChannel)' == 'Dev'">Assets/Icons/my_dev_icon.svg</UnoIconForegroundFile>` |

With generation left on, a Dev-specific foreground gets badged too.

## Warnings

| Code | Meaning |
|---|---|
| `DEVASSETS001` | The SVG is missing, malformed, or has no usable `viewBox`, `width` or `height`. The build uses the prod artwork. |
| `DEVASSETS002` | The badge can't fit inside the platform mask at this scale (e.g. a very large `UnoIconForegroundScale`), so it may be clipped. |
| `DEVASSETS003` | `DevBadgeLabel` is neither `DEV` nor `CI`. The build draws `DEV`. |

A cosmetic badge never fails the build.

## Limitations

- **iOS is covered by unit tests only.** The iOS head can't be built on Windows. Resizetizer's Apple icon generator appears
  to share the Android generator's timestamp-only check, so if an iOS Prod build after a Dev build still shows the badge,
  delete that head's `obj/` folder.
- **The Android 12+ splash circle was measured on one device.** A solid probe splash on a Pixel Tablet (Android 17)
  showed a visible circle of radius 0.484 × the splash drawable. At the template's `UnoSplashScreenScale` of 0.85 the
  bottom-centre badge sits well inside it, so a slightly smaller circle on another device wouldn't matter.

## See also

- [versioning.md](./versioning.md): the Dev/Prod `AppChannel` model this hooks into.
- [worktree-identity.md](./worktree-identity.md): why the worktree name stays out of the icon.

## This app's artwork

Show Up's mark is a calendar page crossed off with a marker: a paper page (`#FFFDF7`) with a marker-red header
(`#D9372B`, the light theme's `MarkerBrush`), two binder rings, and a hand-drawn X that pokes out of the page.
It is original artwork; nothing in it comes from a program publisher.

| File | What it is |
|---|---|
| `Assets/Icons/icon_foreground.svg` | The mark, on a transparent 512 × 512 canvas. |
| `Assets/Splash/splash_screen.svg` | The same mark; keep the two files identical. |
| `Assets/Icons/icon.svg` | Opaque background for iOS, tvOS and Android: the paper colour `#F6F1E6`. |
| `Assets/Icons/icon_transparent.svg` | Transparent background for Windows, Desktop and WebAssembly. |

Rules the mark was drawn to, and that a replacement should keep:

- No letters, and only solid fills and fat round strokes, so it survives 16 px (the X and the red band remain).
- Everything sits inside the central two thirds of the canvas, so Android's circular mask (foreground scale 0.6)
  never clips it.
- It carries its own paper page, so it reads on a light taskbar, a dark one, and on the paper splash.

The WebAssembly manifest (`Platforms/WebAssembly/manifest.webmanifest`) uses the same paper colour for
`background_color` and `theme_color`.
