"""Generates the tileable 512 px paper grain PNGs in src/ShowUp/Assets/Paper (stdlib only, deterministic).

Fractal value noise (three octaves, wrapping so the tiles join without a seam) baked into the alpha
channel of a flat tint: warm brown for the light paper, chalk-white for the dark board.

    python tools/generate-paper-grain.py
"""

import math
import os
import random
import struct
import zlib

SIZE = 512
SEED = 5300
OUT_DIR = os.path.join(os.path.dirname(__file__), "..", "src", "ShowUp", "Assets", "Paper")

# (name, RGB tint, peak alpha 0-255). Strengths are tuned by eye against the "plain paper + grain" draft.
VARIANTS = [
    ("paper_grain_light.png", (115, 97, 64), 34),
    ("paper_grain_dark.png", (243, 239, 228), 16),
]

# (cell size in px, weight): 1 px is plain white noise, larger cells are the softer fibres.
OCTAVES = [(1, 0.5), (2, 0.3), (4, 0.2)]


def smooth(t):
    return t * t * (3 - 2 * t)


def value_noise(rnd, cell):
    n = SIZE // cell
    lattice = [[rnd.random() for _ in range(n)] for _ in range(n)]
    field = []
    for y in range(SIZE):
        gy, fy = divmod(y, cell)
        ty = smooth(fy / cell)
        row = []
        for x in range(SIZE):
            gx, fx = divmod(x, cell)
            tx = smooth(fx / cell)
            x1, y1 = (gx + 1) % n, (gy + 1) % n
            top = lattice[gy][gx] * (1 - tx) + lattice[gy][x1] * tx
            bottom = lattice[y1][gx] * (1 - tx) + lattice[y1][x1] * tx
            row.append(top * (1 - ty) + bottom * ty)
        field.append(row)
    return field


def noise():
    rnd = random.Random(SEED)
    fields = [(value_noise(rnd, cell), weight) for cell, weight in OCTAVES]
    return [[sum(f[y][x] * w for f, w in fields) for x in range(SIZE)] for y in range(SIZE)]


def chunk(tag, data):
    body = tag + data
    return struct.pack(">I", len(data)) + body + struct.pack(">I", zlib.crc32(body) & 0xFFFFFFFF)


LEVELS = 8  # Alpha steps: the grain is so faint that more levels only cost bytes.


def write_png(path, tint, peak, field):
    lo = min(min(r) for r in field)
    hi = max(max(r) for r in field)
    # 4-bit palette: entry i is the tint at alpha peak * i / (LEVELS - 1), so the color stays exact and tiny.
    plte = bytes(c for _ in range(LEVELS) for c in tint)
    trns = bytes(round(peak * i / (LEVELS - 1)) for i in range(LEVELS))
    raw = bytearray()
    for row in field:
        raw.append(0)  # filter: none
        # Squared so most of the tile is faint and only a few specks stand out.
        idx = [round((LEVELS - 1) * ((v - lo) / (hi - lo)) ** 2) for v in row]
        for i in range(0, len(idx), 2):
            raw.append((idx[i] << 4) | idx[i + 1])

    png = bytes([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A])
    png += chunk(b"IHDR", struct.pack(">IIBBBBB", SIZE, SIZE, 4, 3, 0, 0, 0))
    png += chunk(b"PLTE", plte)
    png += chunk(b"tRNS", trns)
    png += chunk(b"IDAT", zlib.compress(bytes(raw), 9))
    png += chunk(b"IEND", b"")
    with open(path, "wb") as f:
        f.write(png)


def main():
    os.makedirs(OUT_DIR, exist_ok=True)
    field = noise()
    for name, tint, peak in VARIANTS:
        path = os.path.join(OUT_DIR, name)
        write_png(path, tint, peak, field)
        print(f"{name}: {os.path.getsize(path)} bytes")


if __name__ == "__main__":
    main()
