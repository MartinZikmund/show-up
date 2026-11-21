# Show Up

**Cross off your home workouts like a marker on a paper calendar.**

Show Up is a home-workout tracker built for accountability. Pick a program, and every finished
workout gets crossed off with a hand-drawn marker X, with sound and a haptic tick. Two-a-day?
Each session is checked off on its own: the first draws "/", the last completes the X. Squeezed in
a quick bonus workout outside your program? Log it without breaking anything.

- Program calendars for popular home programs, plus custom schedules
- Reminders, streaks and history
- Offline-first, with optional sync across devices
- Android, iOS, Windows, macOS, Linux and the web, from one [Uno Platform](https://platform.uno/) codebase

> 🏗️ **Built in public.** Follow progress through the [issues](https://github.com/MartinZikmund/show-up/issues)
> and [pull requests](https://github.com/MartinZikmund/show-up/pulls). The design lives in
> [docs/superpowers/specs](./docs/superpowers/specs).

> **Disclaimer:** Show Up is an independent app. It is not affiliated with, endorsed by, or sponsored by
> Bodi (formerly Beachbody) or any program creator. Program names are trademarks of their respective
> owners and are used only to identify the workout schedules they describe.

Created from the [Uno App template](https://github.com/MartinZikmund/uno-app-template).

## Getting started

```bash
dotnet tool restore                                  # XAML Styler, once per clone
dotnet build src/ShowUp/ShowUp.csproj      # add -f <tfm> to build one platform
dotnet test tests/ShowUp.Core.Tests/ShowUp.Core.Tests.csproj
```

Target frameworks, prerequisites and run commands are in [docs/building.md](./docs/building.md).
The rest of the documentation starts at [docs/README.md](./docs/README.md).

## License

[MIT](./LICENSE). The template's own notice is kept in `docs/TEMPLATE-LICENSE.txt`.

## Staying up to date

`.template-state.json` records the template version and features this app was created with. Keep it
committed: it's how you pick up later template improvements. See
[docs/template-upgrades.md](./docs/template-upgrades.md).
