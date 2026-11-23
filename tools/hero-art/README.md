# Hero art generator

Draws the bundled hero image of every catalog program: `catalog/heroes/<program id>.jpg`, 1200 × 675.

```bash
python tools/hero-art/generate.py              # regenerate all of them
python tools/hero-art/generate.py --svg out/   # and keep the SVG sources to look at
```

Needs Python 3 and [ImageMagick](https://imagemagick.org) (`magick` on PATH). Nothing runs at build time: the
JPEGs are committed.

## What it draws

Original abstract art, so there is nothing to license and nothing that belongs to a program's publisher:

- a diagonal gradient of the program's `accentColor`, darkest on the left, where the app draws the name;
- a motif picked by the kind of workout the program has most of: slanted speed lines for cardio, stacked plates
  for strength, a calendar grid with one day crossed off for everything else;
- no text, no people, no logos.

The composition is seeded by the program id, so a re-run draws the same picture. After changing the script, run it
and commit whatever changed.

## Adding a program

1. Add its JSON to `catalog/programs/` with `"heroImage": "Assets/heroes/<id>.jpg"`.
2. Run the generator and commit the new JPEG.

`CatalogFolderTests` fails when a program has no hero, when a hero has no program, or when one is over 150 KB.
Real artwork can replace a generated image at any time: keep the file name, stay under 150 KB.
