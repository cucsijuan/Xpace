#!/usr/bin/env python3
# Makes the tiling surfaces of assets/surfaces: each an albedo (a grey near 1,
# multiplied by the material's colour), a roughness and a normal map.
#
#   python3 tools/surfaces/make_surfaces.py
#
# Everything is noise filtered in frequency space, so every map tiles exactly.
# The same seed gives the same files. Needs numpy and Pillow.

import os
import numpy as np
from PIL import Image

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
OUT = os.path.join(ROOT, "assets", "surfaces")
SIZE = 1024


def noise(rng, along, across):
    """Tiling noise of unit deviation, with features about so many pixels long (x) and wide (y)."""
    fy = np.fft.fftfreq(SIZE)[:, None]
    fx = np.fft.rfftfreq(SIZE)[None, :]
    keep = np.exp(-2 * (np.pi ** 2) * ((fx * along) ** 2 + (fy * across) ** 2))
    n = np.fft.irfft2(np.fft.rfft2(rng.standard_normal((SIZE, SIZE))) * keep, s=(SIZE, SIZE))
    return n / n.std()


def normals(height):
    """Height in pixels of the map to a normal map (Godot's: green up)."""
    dx = (np.roll(height, -1, 1) - np.roll(height, 1, 1)) / 2
    dy = (np.roll(height, -1, 0) - np.roll(height, 1, 0)) / 2
    n = np.dstack((-dx, dy, np.ones_like(height)))
    n /= np.linalg.norm(n, axis=2, keepdims=True)
    return n * 0.5 + 0.5


def save(name, map_name, values):
    os.makedirs(os.path.join(OUT, name), exist_ok=True)
    data = (np.clip(values, 0, 1) * 255 + 0.5).astype(np.uint8)
    Image.fromarray(data).save(os.path.join(OUT, name, map_name + ".png"), optimize=True)


def brushed_metal():
    """Fine lines along U, from the brush; a little broad unevenness."""
    rng = np.random.default_rng(1)
    fine = noise(rng, 120, 0.8)
    wide = noise(rng, 300, 3)
    broad = noise(rng, 220, 90)
    lines = 0.7 * fine + 0.3 * wide
    save("brushed_metal", "albedo", 0.95 + 0.02 * lines + 0.005 * broad)
    save("brushed_metal", "roughness", 0.56 + 0.05 * lines + 0.012 * broad)
    save("brushed_metal", "normal", normals(0.22 * fine))


def floor_paint():
    """Thick paint: a slight orange peel, faint blotches, and scuffs worn shinier."""
    rng = np.random.default_rng(2)
    peel = noise(rng, 5, 5)
    grain = noise(rng, 1, 1)
    blotch = noise(rng, 90, 90)
    # Scuffs: long thin marks, seen only where a broader noise lets them through
    marks = noise(rng, 70, 1.5)
    patches = noise(rng, 130, 130)
    scuff = np.clip(marks - 1.3, 0, 1) * np.clip(patches, 0, 1)
    save("floor_paint", "albedo", 0.94 + 0.02 * blotch + 0.008 * grain + 0.10 * scuff)
    save("floor_paint", "roughness", 0.84 + 0.03 * blotch + 0.015 * grain - 0.25 * scuff)
    save("floor_paint", "normal", normals(0.9 * peel + 0.1 * grain))


brushed_metal()
floor_paint()
