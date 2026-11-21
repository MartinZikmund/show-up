"""Synthesizes the marker squeak WAVs in src/ShowUp/Assets/Sounds (stdlib only).

Same recipe as squeak() in docs/prototypes/marker-cross.html: white noise through a
band-pass swept 1800 -> 3400 Hz, plus a faint rising triangle squeak. marker_scratch.wav is
scratchStart()'s loop: 1 s of noise band-passed at 2400 Hz (Q 1.2), crossfaded so it loops
without a click; the app sets its volume and rate from the pen speed.

    python tools/generate-marker-sounds.py
"""

import math
import os
import random
import struct
import wave

RATE = 44100
PEAK = 0.30  # Keep it quiet: a marker, not a whistle.
SQUEAK_LEVEL = 4  # Prototype default, 0-10.
OUT_DIR = os.path.join(os.path.dirname(__file__), "..", "src", "ShowUp", "Assets", "Sounds")


def envelope(t, d):
    # 0 -> .35 in 20 ms, ease to .3 at 70 %, release to 0 at the end.
    if t < 0.02:
        return 0.35 * t / 0.02
    if t < d * 0.7:
        return 0.35 - 0.05 * (t - 0.02) / max(d * 0.7 - 0.02, 1e-9)
    return 0.3 * max(0.0, (d - t) / (d * 0.3))


def squeak(seed, ms, f_from=1800.0, f_to=3400.0):
    rnd = random.Random(seed)
    d = ms / 1000.0
    n = int(RATE * d)
    q = 1.4
    x1 = x2 = y1 = y2 = 0.0
    osc_from = 900 + rnd.random() * 300
    osc_to = 1500 + rnd.random() * 500
    phase = 0.0
    out = []
    for i in range(n):
        t = i / RATE
        # RBJ band-pass (constant 0 dB peak), coefficients follow the sweep.
        f0 = f_from + (f_to - f_from) * t / d
        w0 = 2 * math.pi * f0 / RATE
        alpha = math.sin(w0) / (2 * q)
        a0 = 1 + alpha
        b0, b2 = alpha / a0, -alpha / a0
        a1, a2 = -2 * math.cos(w0) / a0, (1 - alpha) / a0
        x0 = rnd.random() * 2 - 1
        y0 = b0 * x0 + b2 * x2 - a1 * y1 - a2 * y2
        x2, x1, y2, y1 = x1, x0, y1, y0
        sample = y0 * envelope(t, d)

        freq = osc_from + (osc_to - osc_from) * t / d
        phase = (phase + freq / RATE) % 1.0
        tri = 4 * abs(phase - 0.5) - 1
        osc_gain = 0.012 * SQUEAK_LEVEL * (t / 0.03 if t < 0.03 else max(0.0, (d - t) / (d - 0.03)))
        out.append(sample + tri * osc_gain)
    return out


def scratch(seed, seconds=1.0, fade=0.1, f0=2400.0, q=1.2):
    rnd = random.Random(seed)
    n, m = int(RATE * seconds), int(RATE * fade)
    w0 = 2 * math.pi * f0 / RATE
    alpha = math.sin(w0) / (2 * q)
    a0 = 1 + alpha
    b0, b2 = alpha / a0, -alpha / a0
    a1, a2 = -2 * math.cos(w0) / a0, (1 - alpha) / a0
    x1 = x2 = y1 = y2 = 0.0
    raw = []
    for _ in range(n + m + RATE // 10):
        x0 = rnd.random() * 2 - 1
        y0 = b0 * x0 + b2 * x2 - a1 * y1 - a2 * y2
        x2, x1, y2, y1 = x1, x0, y1, y0
        raw.append(y0)
    raw = raw[RATE // 10:]  # Drop the filter's settling.
    # Equal-power crossfade of the tail into the head: the loop point is seamless.
    out = raw[:n]
    for i in range(m):
        t = i / m
        out[i] = raw[i] * math.sin(t * math.pi / 2) + raw[n + i] * math.cos(t * math.pi / 2)
    return out


def write(name, samples):
    peak = max(abs(s) for s in samples) or 1.0
    scale = PEAK / peak
    path = os.path.join(OUT_DIR, name)
    with wave.open(path, "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(RATE)
        w.writeframes(b"".join(struct.pack("<h", int(max(-1.0, min(1.0, s * scale)) * 32767)) for s in samples))
    print(f"wrote {os.path.relpath(path)} ({len(samples)} samples)")


def main():
    os.makedirs(OUT_DIR, exist_ok=True)
    write("marker_1.wav", squeak(1, 180))
    write("marker_2.wav", squeak(2, 180, 1700, 3200))
    write("marker_3.wav", squeak(3, 180, 1900, 3600))
    write("marker_tick.wav", squeak(4, 70))
    write("marker_scratch.wav", scratch(5))


if __name__ == "__main__":
    main()
