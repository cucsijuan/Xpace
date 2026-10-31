#!/usr/bin/env python3
# The relief of the landing platform's deck, in normal maps.
#
#   python3 tools/station_bay/pad_maps.py
#
# joints_normal.png covers the deck once: the joints (pad_joints.json, from
# build_pad.py) sunk, their edges sloping down into them.
# joints_albedo.png: a joint's edges in the shade of the panels beside it.
# The dimples of the central plate are shallow bowls, covering the plate once
# (its second UV).
# dimples_albedo.png: how much a bowl darkens the plate; its alpha is where the
# bowls are, and in the material it also lets the bowls' normal through.
# dimples_normal.png: each bowl a piece of a sphere. Needs numpy and Pillow.

import json
import os
import numpy as np
from PIL import Image, ImageDraw
from pad_layout import DECK_HALF, JOINT_DEPTH, JOINT_SOFT, PLATE_HALF, DIMPLE_RADIUS, dimples

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
OUT = os.path.join(ROOT, "assets", "stations", "pad")
SIZE = 2048
RIM_SLOPE = 2.2         # the bowl's slope at its rim (1 = 45 degrees)
DARK_RIM, DARK_MIDDLE = 0.7, 0.2    # the albedo's multiplier

metres = 2 * PLATE_HALF / SIZE
col, row = np.meshgrid(np.arange(SIZE) + 0.5, np.arange(SIZE) + 0.5)
x = (col - SIZE / 2) * metres
y = (SIZE / 2 - row) * metres

slope_x = np.zeros((SIZE, SIZE))
slope_y = np.zeros((SIZE, SIZE))
dark = np.ones((SIZE, SIZE))
alpha = np.zeros((SIZE, SIZE))
for cx, cy in dimples():
    dx, dy = (x - cx) / DIMPLE_RADIUS, (y - cy) / DIMPLE_RADIUS
    r = np.hypot(dx, dy)
    inside = r <= 1.0 + 2 * metres / DIMPLE_RADIUS
    # A sphere's surface tilts in proportion to the distance from the bowl's middle, towards it
    slope_x[inside] = -dx[inside] * RIM_SLOPE
    slope_y[inside] = -dy[inside] * RIM_SLOPE
    dark[inside] = DARK_MIDDLE + (DARK_RIM - DARK_MIDDLE) * np.clip(r[inside], 0, 1) ** 2
    alpha[inside] = np.clip((1.0 - r[inside]) * DIMPLE_RADIUS / metres + 0.5, 0, 1)

n = np.dstack((slope_x, slope_y, np.ones((SIZE, SIZE))))
n /= np.linalg.norm(n, axis=2, keepdims=True)
os.makedirs(OUT, exist_ok=True)
Image.fromarray(((n * 0.5 + 0.5) * 255 + 0.5).astype(np.uint8)).save(os.path.join(OUT, "dimples_normal.png"), optimize=True)
grey = (dark * 255 + 0.5).astype(np.uint8)
Image.fromarray(np.dstack((grey, grey, grey, (alpha * 255 + 0.5).astype(np.uint8)))).save(os.path.join(OUT, "dimples_albedo.png"), optimize=True)

# The joints: their mask, blurred at the edges, is how far down the deck goes
JOINT_SIZE = 2048
SAMPLES = 4
metres = 2 * DECK_HALF / JOINT_SIZE
with open(os.path.join(os.path.dirname(os.path.abspath(__file__)), "pad_joints.json")) as source:
    outlines = json.load(source)
big = Image.new("L", (JOINT_SIZE * SAMPLES, JOINT_SIZE * SAMPLES), 0)
draw = ImageDraw.Draw(big)
for outline in outlines:
    draw.polygon([((px / (2 * DECK_HALF) + 0.5) * JOINT_SIZE * SAMPLES, (0.5 - py / (2 * DECK_HALF)) * JOINT_SIZE * SAMPLES)
                  for px, py in outline], fill=255)
mask = np.asarray(big.resize((JOINT_SIZE, JOINT_SIZE), Image.BOX)).astype(float) / 255
fy = np.fft.fftfreq(JOINT_SIZE)[:, None]
fx = np.fft.rfftfreq(JOINT_SIZE)[None, :]
sigma = JOINT_SOFT / metres
soft = np.fft.irfft2(np.fft.rfft2(mask) * np.exp(-2 * (np.pi * sigma) ** 2 * (fx ** 2 + fy ** 2)), s=mask.shape)
height = -soft * JOINT_DEPTH / metres       # in pixels of the map
ddx = (np.roll(height, -1, 1) - np.roll(height, 1, 1)) / 2
ddy = (np.roll(height, -1, 0) - np.roll(height, 1, 0)) / 2
n = np.dstack((-ddx, ddy, np.ones_like(height)))
n /= np.linalg.norm(n, axis=2, keepdims=True)
Image.fromarray(((n * 0.5 + 0.5) * 255 + 0.5).astype(np.uint8)).save(os.path.join(OUT, "joints_normal.png"), optimize=True)
inner = np.clip((soft - 0.5) * 2, 0, 1)
shade = (0.45 + 0.55 * inner * inner * (3 - 2 * inner)) * 255 + 0.5
Image.fromarray(np.clip(shade, 0, 255).astype(np.uint8)).save(os.path.join(OUT, "joints_albedo.png"), optimize=True)
