# Marking: the marker X and the cross moment

Crossing off a workout draws a hand-drawn marker X, squeaks, and ticks the phone. Crossing off
today also plays the "cross moment": the X drawn big, full screen on phones and as a popup on
wider windows. The feel reference is [`prototypes/marker-cross.html`](./prototypes/marker-cross.html).

## Drawing an X

`MarkerCrossControl` (`src/ShowUp/Controls/MarkerCrossControl.cs`) draws up to two strokes:

```xml
<controls:MarkerCrossControl Seed="{x:Bind Seed}" Strokes="{x:Bind Strokes, Mode=OneWay}" />
```

| Property | Default | Meaning |
| --- | --- | --- |
| `Strokes` | 0 | 0 = empty, 1 = "/" (top-left to bottom-right), 2 = full X |
| `Seed` | 0 | Picks the wobble. Use `MarkerGeometry.SeedFrom(completionId)` so a cross looks the same forever |
| `MarkerBrush` | `{ThemeResource MarkerBrush}` | Red on paper, chalk on the dark theme |
| `StrokeThicknessScale` | 1 | Multiplies the computed width |
| `AnimateChanges` | true | Animate increases; decreases always redraw instantly |

Each stroke is two `Path`s: the main line at `9 × min(w, h) / 96` (never below 0.6×) and 90%
opacity, and a 1.35× wider under-stroke at 35% for ink bleed. An increase animates
`StrokeDashOffset` over 180 ms with ease-out, the second stroke 90 ms after the first on a 0 → 2
jump. With animations off in the OS (`UISettings.AnimationsEnabled`) the X lands instantly.

The geometry lives in Core so it's testable: `MarkerGeometry.Create(seed, width, height)` is a
straight port of the prototype's `strokePath`, including its seeded generator, so a seed draws the
same X on every head. `SeedFrom(Guid)` is FNV-1a over the GUID bytes, never `GetHashCode`.

## Sound and haptics

`IMarkingFeedback` (Core) is implemented by `Services/Marking/MarkingFeedback.cs`:

- `StrokeDrawn()` at the start of every stroke: a random `marker_{1,2,3}.wav` plus a 12 ms tick.
- `SessionTicked()` for a 3+ session day that gained a session but no stroke: `marker_tick.wav` + 8 ms.
- `CrossLanded()` when the cross moment's X completes: a 25 ms tick.

Both toggles persist through `IAppPreferences` (`MarkerSoundEnabled`, `MarkerHapticsEnabled`, on by
default) and show in Settings → Crossing off. Failures never break a cross: the first playback
error logs a warning and the head goes silent.

| Head | Sound | Haptics |
| --- | --- | --- |
| Windows (WinUI) | `MediaPlayer`, `AudioCategory = SoundEffects` | none |
| Desktop on Windows | winmm `PlaySound` (Uno's desktop `MediaPlayer` needs the LibVLC-based `MediaPlayerElement` feature) | none |
| Desktop on macOS/Linux | silent | none |
| WebAssembly | `MediaPlayer` (HTML audio) | none |
| Android | `SoundPool`, usage `AssistanceSonification`, no audio focus request | `VibrationDevice` (needs `VIBRATE`, declared in the manifest) |
| iOS | `AVAudioPlayer`, session category `Ambient` + `MixWithOthers` | `VibrationDevice` |

**The squeak must never pause or duck the user's workout music.** That's why the phones skip Uno's
`MediaPlayer`: on iOS it forces the `Playback` session category (which stops other audio), and on
Android a sound-effect pool is lighter and never asks for audio focus. `Ambient` also means the iOS
silent switch mutes the squeak, like any other UI sound.

Asset names use only lowercase letters, digits and underscores: Android rewrites anything else in
asset names (`SoundAssets_UseAndroidSafeNames` guards it).

The sounds are synthesized by [`tools/generate-marker-sounds.py`](../tools/generate-marker-sounds.py)
and released as CC0 (see `src/ShowUp/Assets/Sounds/LICENSE.md`).

## The cross moment

`ICrossMoment.ShowAsync(CrossMomentInfo)` (Core) completes when the moment is dismissed. It is a
scoped service that forwards to the `CrossMomentOverlay` hosted once in `WindowShell`, above the
padded content, so it covers the tab bar and safe areas.

- Below 640 px it fills the window on paper; from 640 px it's a 480 px card over a dimmed backdrop.
- The big cell is ~70% of the shorter side and draws from `StrokesBefore` to `StrokesAfter`.
- The caption fades in after the last stroke and is announced (`LiveSetting=Assertive`).
- Tap, click, Esc, Enter or Space dismiss; otherwise it leaves 2.5 s after the last stroke.

Callers decide whether to show it with `CrossMomentPolicy.ShouldShow(mode, isToday)` and the
user's `IAppPreferences.CrossMomentMode` (`OnlyToday` by default, or `Always` / `Never`). After the
moment, set the in-cell X without animation.

## Debug tools

Debug builds add a sample cell, a "Preview cross moment" button and a 25× slow-motion toggle to
the Today page, until the calendar and Today card land.
