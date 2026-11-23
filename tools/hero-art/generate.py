"""Generates the bundled program hero art: one original abstract composition per catalog program.

    python tools/hero-art/generate.py            # writes catalog/heroes/<program id>.jpg
    python tools/hero-art/generate.py --svg DIR  # also keeps the SVG sources in DIR

Needs ImageMagick (`magick`) on PATH. See README.md next to this file.
"""
import argparse
import glob
import hashlib
import json
import os
import random
import subprocess
import sys
import tempfile
from collections import Counter

WIDTH, HEIGHT = 1200, 675
QUALITY = 82
MAX_BYTES = 150 * 1024


def rgb(color):
    color = color.lstrip('#')
    return tuple(int(color[i:i + 2], 16) for i in (0, 2, 4))


def mix(color, other, amount):
    """`color` moved `amount` (0-1) of the way to `other`."""
    a, b = rgb(color), rgb(other)
    return '#%02X%02X%02X' % tuple(round(a[i] + (b[i] - a[i]) * amount) for i in range(3))


def dominant_kind(program, kinds):
    counts = Counter(
        kinds.get(session['workoutId'], 'mixed')
        for phase in program['tracks'][0]['phases'] for week in phase['weeks'] for day in week['days'] for session in day['sessions'])
    counts.pop('rest', None)
    top = counts.most_common(1)[0][0] if counts else 'mixed'
    return top if top in ('cardio', 'strength') else 'mixed'


def speed_lines(rnd, light):
    """Cardio: slanted bars streaming to the right."""
    shapes = []
    for _ in range(rnd.randint(11, 14)):
        x = rnd.uniform(430, 1180)
        y = rnd.uniform(40, 620)
        length = rnd.uniform(140, 520)
        thickness = rnd.uniform(14, 46)
        shapes.append(
            f'<rect x="{x:.1f}" y="{y:.1f}" width="{length:.1f}" height="{thickness:.1f}" rx="{thickness / 2:.1f}" '
            f'fill="{light}" opacity="{rnd.uniform(0.14, 0.5):.2f}" transform="rotate(-24 {x:.1f} {y:.1f})"/>')
    return shapes


def stacked_blocks(rnd, light):
    """Strength: columns of stacked plates rising from the floor."""
    shapes = []
    x = rnd.uniform(520, 580)
    while x < 1150:
        width = rnd.uniform(74, 132)
        y = 620.0
        for _ in range(rnd.randint(2, 6)):
            height = rnd.uniform(38, 96)
            y -= height + 10
            shapes.append(
                f'<rect x="{x:.1f}" y="{y:.1f}" width="{width:.1f}" height="{height:.1f}" rx="12" '
                f'fill="{light}" opacity="{rnd.uniform(0.16, 0.5):.2f}"/>')
        x += width + rnd.uniform(18, 34)
    return shapes


def calendar_grid(rnd, light):
    """Mixed: a calendar's grid of days, some filled, one crossed off."""
    shapes = []
    cell, gap, left, top = 92, 16, 520, 70
    crossed = (rnd.randint(1, 4), rnd.randint(1, 3))
    for row in range(5):
        for column in range(6):
            x, y = left + column * (cell + gap), top + row * (cell + gap)
            shapes.append(
                f'<rect x="{x}" y="{y}" width="{cell}" height="{cell}" rx="14" fill="{light}" opacity="{rnd.choice((0.1, 0.1, 0.18, 0.3, 0.44)):.2f}"/>')
            if (column, row) == crossed:
                shapes.append(
                    f'<g fill="none" stroke="{light}" stroke-width="13" stroke-linecap="round" opacity="0.85">'
                    f'<path d="M{x + 22} {y + 24} L{x + 70} {y + 70}"/><path d="M{x + 72} {y + 20} L{x + 20} {y + 72}"/></g>')
    return shapes


MOTIFS = {'cardio': speed_lines, 'strength': stacked_blocks, 'mixed': calendar_grid}


def compose(program, kind):
    accent = program['accentColor']
    rnd = random.Random(int(hashlib.sha256(program['id'].encode()).hexdigest()[:12], 16))
    shapes = MOTIFS[kind](rnd, mix(accent, '#FFFFFF', 0.55))
    return '\n'.join([
        f'<svg xmlns="http://www.w3.org/2000/svg" width="{WIDTH}" height="{HEIGHT}" viewBox="0 0 {WIDTH} {HEIGHT}">',
        '<defs>',
        f'<linearGradient id="bg" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="{mix(accent, "#000000", 0.62)}"/>'
        f'<stop offset="1" stop-color="{mix(accent, "#000000", 0.2)}"/></linearGradient>',
        # The program name is drawn over the left side by the app: keep it dark and calm.
        '<linearGradient id="calm" x1="0" y1="0" x2="1" y2="0"><stop offset="0" stop-color="#000000" stop-opacity="0.5"/>'
        '<stop offset="0.5" stop-color="#000000" stop-opacity="0"/></linearGradient>',
        '</defs>',
        f'<rect width="{WIDTH}" height="{HEIGHT}" fill="url(#bg)"/>',
        *shapes,
        f'<rect width="{WIDTH}" height="{HEIGHT}" fill="url(#calm)"/>',
        '</svg>',
    ])


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--svg', help='also write the SVG sources into this folder')
    args = parser.parse_args()

    root = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
    catalog = os.path.join(root, 'catalog')
    workouts = json.load(open(os.path.join(catalog, 'workouts.json'), encoding='utf-8'))
    workouts = workouts['workouts'] if isinstance(workouts, dict) else workouts
    kinds = {w['id']: w['kind'] for w in workouts}
    out_dir = os.path.join(catalog, 'heroes')
    os.makedirs(out_dir, exist_ok=True)
    if args.svg:
        os.makedirs(args.svg, exist_ok=True)

    failed = False
    for path in sorted(glob.glob(os.path.join(catalog, 'programs', '*.json'))):
        program = json.load(open(path, encoding='utf-8'))
        kind = dominant_kind(program, kinds)
        svg = compose(program, kind)
        target = os.path.join(out_dir, program['id'] + '.jpg')
        with tempfile.TemporaryDirectory() as temp:
            source = os.path.join(args.svg or temp, program['id'] + '.svg')
            with open(source, 'w', encoding='utf-8', newline='\n') as handle:
                handle.write(svg)
            # -strip keeps the bytes free of timestamps, so a re-run leaves git clean.
            subprocess.run(['magick', source, '-strip', '-interlace', 'none', '-sampling-factor', '4:2:0', '-quality', str(QUALITY), target], check=True)
        size = os.path.getsize(target)
        too_big = size > MAX_BYTES
        failed = failed or too_big
        print(f'{program["id"]:28} {kind:9} {size / 1024:6.1f} KB{"  TOO BIG" if too_big else ""}')
    return 1 if failed else 0


if __name__ == '__main__':
    sys.exit(main())
