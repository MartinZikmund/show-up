# Marking: the marker X, drawing it yourself, and the cross moment

Crossing off a workout draws a hand-drawn marker X, squeaks, and ticks the phone. By default the
user draws the cross themselves on a big square ("draw it yourself"); the auto marker X is the
alternative. Crossing off today also plays the "cross moment": the X drawn big, full screen on
phones and as a popup on wider windows. The feel reference is
[`prototypes/marker-cross.html`](./prototypes/marker-cross.html).

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
| `Inks` | null | The user's drawn crosses (`InkCodec` JSON), one per session crossed by drawing |
| `BleedOpacity` | `{ThemeResource MarkerBleedOpacity}` | Opacity of the wider bleed under each stroke |

A mixed day (some sessions auto, some drawn) draws both: `Strokes` counts only the generated
strokes, so set it from the auto-crossed sessions, and put each drawn session's `InkJson` in
`Inks`. Ink is scaled to the whole control and rendered by `InkView` with the same two layers.

Each stroke is three `Path`s: the main line at `9 × min(w, h) / 96` (never below 0.6×, then a
seeded ±8% per stroke from `MarkerGeometry.WidthFactor`) at 90% opacity; a 1.35× wider bleed,
drawn underneath both strokes, at `MarkerBleedOpacity` (0.35 on paper and in high contrast, 0.18 on
the chalkboard, where chalk white at 35% read as a grey rim); and a thin (0.35×) streak at 50%, offset sideways by 0.2× the
width, for ink texture. No filters, so it costs the same on every head. An increase animates
`StrokeDashOffset` over 180 ms with ease-out, the second stroke 90 ms after the first on a 0 → 2
jump. With animations off in the OS (`UISettings.AnimationsEnabled`) the X lands instantly.

The geometry lives in Core so it's testable: `MarkerGeometry.Create(seed, width, height)` is a
straight port of the prototype's `strokePath`, including its seeded generator, so a seed draws the
same X on every head. The control calls `ForCell`, which scales wobble and overshoot like the width
(`max(1, min(w, h) / 96)`), so a big X stays as loose as a small one; at 96 px it matches the
prototype exactly. `SeedFrom(Guid)` is FNV-1a over the GUID bytes, never `GetHashCode`.

## Draw it yourself

`IAppPreferences.CrossOffStyle` (Settings → Crossing off → Cross-off style) picks `DrawYourself`
(default) or `AutoMarker` (tap = generated X, cross moment per `CrossMomentMode`). In draw mode
callers open the square with `ICrossMoment.DrawAsync`:

```csharp
using ShowUp.Core.Marking;

var result = await crossMoment.DrawAsync(new CrossMomentDrawRequest(info, priorInks));
if (result.Crossed)
{
    // InkJson is null for "Do it for me" (the generated X).
    await repository.CrossSessionAsync(enrollmentId, dayIndex, sessionIndex, dayDate, result.InkJson);
}
```

`DrawAsync` completes as soon as the user decides (drawn, Do it for me, or cancelled), so the caller
persists right away; the caption then stays up and dismisses itself. `info` carries the caption
numbers as they will be after this cross; `priorInks` are the day's other drawn sessions, shown at
50%.

- **The square.** `InkPad` (`Controls/InkPad.cs`) covers the big cell. It captures one pointer
  (finger, pen, or the left mouse button), normalizes points to 0..1 and feeds an `InkSession`.
  Moves under 0.004 are dropped. Strokes render live through `InkView`: midpoint quadratic smoothing
  (`InkPath.Smooth`), width `9 × max(0.6, min(w, h) / 96)`, main at 90% over a 1.35× bleed.
- **When it counts.** `InkScore.Of` is the prototype formula: total length `len`, spread
  `len > 0.2 ? min(Δx / 0.4, Δy / 0.4, 1) : 0`, score `min(len / 0.75, 1) × 0.6 + spread × 0.4`.
  It counts at 1: a big diagonal, an X or a scribble do; a short tick or a flat horizontal line
  don't. The meter under the square shows the score.
- **The pause.** On lift, if the ink counts, a **Done** button appears and `InkSession` arms a
  900 ms settle timer (an injected `TimeProvider`, so it's unit-tested). A new stroke or **Undo**
  cancels it, which is what lets the user lift between the two halves of an X. When it fires, or
  Done is tapped, the cross is taken: `DayTaken()` (a squeak + 30 ms haptic), the caption fades in,
  and the moment leaves after 2.2 s. Taps within 400 ms of finishing are ignored.
- **Do it for me** (focused on open, so Enter takes it) plays the normal animated moment and returns
  `CrossMomentOutcome.Auto`. **Cancel**, Esc or a backdrop tap return `Cancelled`.
- **Screen readers** hear "Draw to cross off, or press Do it for me" on open (a live region), and
  the square carries the same hint.

### Stored ink

`InkCodec.Encode` stores strokes as compact JSON, one array per stroke: `[[x,y,x,y,…],…]`. Each
stroke is simplified with Ramer–Douglas–Peucker (epsilon 0.004), capped at 600 points in total
(extra points dropped evenly, ends kept) and quantized to 3 decimals; a hand-drawn X stays well under
500 bytes. It lives in `CompletionEntity.InkJson` (migration v2, see [storage.md](./storage.md)) and
comes back on `CompletedSession.InkJson`. `Decode` never throws: unreadable ink renders as no ink.

## Sound and haptics

`IMarkingFeedback` (Core) is implemented by `Services/Marking/MarkingFeedback.cs`:

- `StrokeDrawn()` at the start of every stroke: a random `marker_{1,2,3}.wav` plus a 12 ms tick.
- `SessionTicked()` for a 3+ session day that gained a session but no stroke: `marker_tick.wav` + 8 ms.
- `CrossLanded()` when the cross moment's X completes: a 25 ms tick.
- `ScratchStart()` / `ScratchSpeed(v)` / `ScratchStop()` while drawing: an 8 ms tick, then a looped
  `marker_scratch.wav` (band-passed noise) whose volume and pitch follow pen speed (`ScratchMix`:
  the prototype's `min(.32, v × .9)` gain and 1800–4000 Hz band). It goes silent 60 ms after the pen
  stops moving.
- `DayTaken()` when a drawn cross is accepted: a squeak + 30 ms haptic.

Both toggles persist through `IAppPreferences` (`MarkerSoundEnabled`, `MarkerHapticsEnabled`, on by
default) and show in Settings → Crossing off. Failures never break a cross: the first playback
error logs a warning and the head goes silent.

| Head | Sound | Scratch loop | Haptics |
| --- | --- | --- | --- |
| Windows (WinUI) | `MediaPlayer`, `AudioCategory = SoundEffects` | looping `MediaPlayer`: `Volume` + `PlaybackRate` | none |
| Desktop on Windows | winmm `PlaySound` (Uno's desktop `MediaPlayer` needs the LibVLC-based `MediaPlayerElement` feature) | winmm `waveOut` looping buffer + `waveOutSetVolume`, no pitch (most drivers can't change rate). That volume is the app's session volume, so it's restored to full on stop | none |
| Desktop on macOS/Linux | silent | silent | none |
| WebAssembly | `MediaPlayer` (HTML audio) | Web Audio, the prototype's code (`WasmScripts/MarkerScratch.js`): looped noise → band-pass → gain | none |
| Android | `SoundPool`, usage `AssistanceSonification`, no audio focus request | `SoundPool` stream with loop -1, `SetVolume` + `SetRate` | `VibrationDevice` (needs `VIBRATE`, declared in the manifest) |
| iOS | `AVAudioPlayer`, session category `Ambient` + `MixWithOthers` | looping `AVAudioPlayer` (`EnableRate`), `Volume` + `Rate` | `VibrationDevice` |

Where a head can't start the loop (or it failed once), `ScratchStart` plays the short stroke squeak
at the start of each stroke instead. The loop uses the same audio categories as the squeaks, so it
never ducks the user's music either.

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
- The X is inset so it never covers the day number or the title.
- The caption ("Day 10 of 60 · 🔥 9-day streak", the flame a vector `Path` in `MarkerBrush` because
  WebAssembly has no emoji font; the streak part hides at 0) fades in after the last stroke.
- Screen readers hear one plain sentence (`CrossMomentCaption.Announcement`, e.g. "Day 10 of 60,
  9-day streak. 1 of 2 done"): the panel gets it as its name on reveal and raises
  `LiveRegionChanged`. Until then it stays out of the UIA tree.
- Tap, click, Esc, Enter or Space dismiss; otherwise it leaves 2.5 s after the last stroke.

Callers decide whether to show it with `CrossMomentPolicy.ShouldShow(mode, isToday)` and the
user's `IAppPreferences.CrossMomentMode` (`OnlyToday` by default, or `Always` / `Never`). After the
moment, set the in-cell X without animation.

## Debug tools

Debug builds add a sample cell, a "Preview cross moment" button, a "Preview draw it yourself"
button (a two-session day: the drawn ink lands in the sample cell, and the next draw shows it at
50%) and a 25× slow-motion toggle to the Today page, until the calendar and Today card land.
