# Theme: paper, chalkboard and marker color

## Marker color

Settings > Crossing off > Marker color picks the ink: Auto plus six colors (`MarkerColor`,
`ShowUp.Core/Marking`). `MarkerPalette.Resolve` gives the light (paper) and dark (chalkboard) value;
every pair keeps 3:1 contrast against its cell (checked by `MarkerPaletteTests` with `ColorContrast`).

Changing it sends `MarkerColorChangedMessage`; `MarkerBrushUpdater` (head) recolors the `MarkerBrush` and
`MarkerBleedBrush` instances in the Light and Dark theme dictionaries. Every `{ThemeResource MarkerBrush}`
consumer holds those same brushes, so calendar cells, Today cards, the cross moment, drawn ink and the
flame repaint at once. High Contrast ignores the setting. The bottom tab underline uses its own
`TabIndicatorBrush`, so it stays accent.

`SettingsViewModel.CanChangeMarkerColor` is the hook for the Personalization gate.

## Paper background

Plain paper (or chalkboard) plus a faint grain, no grid lines. `PaperBackground` lays out 512 px tiles of
`Assets/Paper/paper_grain_{light,dark}.png` (WinUI has no tiling brush), at most 8x8, and only resizes the
tile set when the window size or the theme's texture changes. The `PaperGrainSource` theme resource picks the texture; High
Contrast sets it to `none` and gets the flat window color.

The PNGs are generated, deterministic and committed: 4-bit palette images with 8 alpha steps, about 80 KB each. To tweak strength or tint, edit `VARIANTS` and rerun:

```
python tools/generate-paper-grain.py
```
