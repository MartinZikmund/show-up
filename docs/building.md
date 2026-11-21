# Building & running

## Restore dotnet tools

The repository ships a local tool manifest (`.config/dotnet-tools.json`). Restore the tools once after cloning:

```bash
dotnet tool restore
```

This installs [XAML Styler](./xaml-styler.md), which CI enforces on every pull request.

## Restore workloads

Restoring a project evaluates all of its target frameworks, so every build needs the workloads of
every head, even with `-f net11.0-desktop`. Without them you get `NETSDK1147`. Install them once
after cloning, and again after adding a head:

```bash
dotnet workload restore src/ShowUp.slnx
```

## Target frameworks

`src/ShowUp` is an Uno single project with one head per platform. The target framework moniker
selects which one, and `TargetFrameworks` in the project file lists the heads this repository has:

| TFM | Platform | Prerequisites |
|---|---|---|
| `net11.0-desktop` | Cross-platform desktop (Windows, macOS, Linux), Skia-rendered | None beyond the workloads above — the fastest head to build |
| `net11.0-browserwasm` | WebAssembly | None beyond the workloads above |
| `net11.0-windows10.0.26100` | Windows, WinAppSDK | Windows + the Windows SDK |
| `net11.0-android` | Android | Android SDK |
| `net11.0-ios` | iOS | A Mac to build against |
| `net11.0-tvos` | tvOS (Apple TV) | A Mac for anything past compiling |

Prefer a single-target build. The default multi-target build pulls the Android SDK, which fails on
network-restricted machines and is rarely what you want while iterating.

## Build and run

```bash
# Cross-platform desktop — the quickest feedback loop
dotnet build src/ShowUp/ShowUp.csproj -f net11.0-desktop
dotnet run   --project src/ShowUp/ShowUp.csproj -f net11.0-desktop

# Windows (WinAppSDK)
dotnet build src/ShowUp/ShowUp.csproj -f net11.0-windows10.0.26100
```

`-f` is mandatory on `dotnet run`. Without it you get the WebAssembly head.

## Running the packaged Windows app

The WinUI head can be built, launched with full package identity, and UI-automated entirely from
the command line — no Visual Studio. It needs the [`winapp`](https://www.nuget.org/packages/Microsoft.WinAppCli)
CLI (`winget install Microsoft.WinAppCli`):

```powershell
$out = Join-Path (Get-Location) "src\ShowUp\bin\Debug\net11.0-windows10.0.26100"
$app = winapp run $out --exe ShowUp.exe --detach --json | ConvertFrom-Json   # non-blocking
$AppPid = $app.ProcessId

winapp ui inspect    -a $AppPid                            # discover element slugs
winapp ui screenshot -a $AppPid --output .screenshots\app.png

Stop-Process -Id $AppPid -Force -ErrorAction SilentlyContinue
winapp unregister --manifest "$out\AppxManifest.xml"
```

`--exe ShowUp.exe` disambiguates from the co-located `RestartAgent.exe`.

Target the **PID**, not the window title, and stop the PID rather than the process name. Both
matter once you use worktrees: a worktree build carries its own package identity and window title,
while `AssemblyName` stays `ShowUp` everywhere — so `Get-Process ShowUp | Stop-Process`
kills every worktree's app.
See [worktree-identity.md](./worktree-identity.md).

The full command set and its gotchas live in
[`.claude/skills/run-winui-app/SKILL.md`](../.claude/skills/run-winui-app/SKILL.md) — that file is
canonical; this is a summary.

## Tests

Unit tests cover `ShowUp.Core`, which is why logic belongs there rather than in the head:

```bash
dotnet test tests/ShowUp.Core.Tests/ShowUp.Core.Tests.csproj
```

The runner is MSTest on Microsoft.Testing.Platform, not VSTest — VSTest-only flags such as
`--logger` and `--nologo` will error out.

## When a build hangs

A `dotnet build` or `dotnet test` that stops producing output while MSBuild processes linger is
usually the local node-reuse deadlock. Re-run with `MSBUILDDISABLENODEREUSE=1` set. Don't pass
`-nodeReuse:false` to `dotnet test` — it silently runs zero tests.

## Android: Debug builds hide a missing INTERNET permission

.NET adds `INTERNET` to **Debug** Android builds so the debugger can connect. Networking therefore
works while you develop and fails only in Release, with
`java.lang.SecurityException: Permission denied (missing INTERNET permission?)`.

This repository declares it in `src/ShowUp/Platforms/Android/AndroidManifest.xml`. A fully
offline app can remove the permission, along with any test that checks for it.
