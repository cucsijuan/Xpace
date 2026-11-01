#!/usr/bin/env python3
# Makes the lettering of FIRECTL's weapon page: heavy condensed letters with cut
# corners. Each glyph is a skeleton of lines thickened by an octagon, which is
# what cuts the outer corners, turned into runs of equal rows.
#
#   python3 tools/firectl_font.py          # rewrites the tables in firectl.asm
#   python3 tools/firectl_font.py out.png  # and draws the pages, 4 x real size
#
# The tables go between the "font tables" marks in software/firectl/firectl.asm,
# since the assembler has no includes. Needs numpy and Pillow.

import os
import sys
import numpy as np
from PIL import Image, ImageDraw

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))
SOURCE = os.path.join(ROOT, "software", "firectl", "firectl.asm")
BEGIN = "; ---- font tables (tools/firectl_font.py) ----"
END = "; ---- end of the font tables ----"

SCREEN_W, SCREEN_H = 112, 160
PALETTE = {0: (2, 8, 3), 1: (40, 130, 52), 2: (92, 255, 104), 3: (255, 52, 36)}

# Skeletons on a unit box, y down: each glyph a list of lines
SKELETONS = {
    "A": [[(0, 1), (0, 0), (1, 0), (1, 1)], [(0, .55), (1, .55)]],
    "C": [[(1, .2), (1, 0), (0, 0), (0, 1), (1, 1), (1, .8)]],
    "D": [[(0, 0), (.7, 0), (1, .25), (1, .75), (.7, 1), (0, 1), (0, 0)]],
    "E": [[(1, 0), (0, 0), (0, 1), (1, 1)], [(0, .48), (.75, .48)]],
    "F": [[(1, 0), (0, 0), (0, 1)], [(0, .48), (.75, .48)]],
    "G": [[(1, 0), (0, 0), (0, 1), (1, 1), (1, .55), (.8, .55)]],
    "H": [[(0, 0), (0, 1)], [(1, 0), (1, 1)], [(0, .48), (1, .48)]],
    "I": [[(.5, 0), (.5, 1)]],
    "K": [[(0, 0), (0, 1)], [(1, 0), (0, .55)], [(.35, .4), (1, 1)]],
    "L": [[(0, 0), (0, 1), (1, 1)]],
    "M": [[(0, 1), (0, 0), (.5, .42), (1, 0), (1, 1)]],
    "N": [[(0, 1), (0, 0), (1, 1), (1, 0)]],
    "O": [[(0, 0), (1, 0), (1, 1), (0, 1), (0, 0)]],
    "R": [[(0, 1), (0, 0), (1, 0), (1, .52), (0, .52)], [(.4, .52), (1, 1)]],
    "S": [[(1, 0), (0, 0), (0, .49), (1, .49), (1, 1), (0, 1)]],
    "T": [[(0, 0), (1, 0)], [(.5, 0), (.5, 1)]],
    "U": [[(0, 0), (0, 1), (1, 1), (1, 0)]],
    "Y": [[(0, 0), (.5, .5), (1, 0)], [(.5, .5), (.5, 1)]],
    "0": [[(0, 0), (1, 0), (1, 1), (0, 1), (0, 0)]],
    "1": [[(.15, .18), (.55, 0), (.55, 1)]],
    "2": [[(0, .18), (0, 0), (1, 0), (1, .49), (0, .49), (0, 1), (1, 1)]],
    "3": [[(0, 0), (1, 0), (1, 1), (0, 1)], [(.3, .48), (1, .48)]],
    "4": [[(0, 0), (0, .6), (1, .6)], [(1, 0), (1, 1)]],
    "5": [[(1, 0), (0, 0), (0, .49), (1, .49), (1, 1), (0, 1)]],
    "6": [[(1, 0), (0, 0), (0, 1), (1, 1), (1, .49), (0, .49)]],
    "7": [[(0, 0), (1, 0), (.4, 1)]],
    "8": [[(0, 0), (1, 0), (1, 1), (0, 1), (0, 0)], [(0, .48), (1, .48)]],
    "9": [[(1, .51), (0, .51), (0, 0), (1, 0), (1, 1), (0, 1)]],
    "-": [[(.1, .5), (.9, .5)]],
}

# name: width, height, half a stroke, how much the corners lose, gap, narrow I, the glyphs
FONTS = {
    "big": (34, 58, 6, 3, 2, 13, "GUNMSLIR"),
    "mid": (14, 24, 2, 1, 2, 6, "0123456789-NOAM"),
    "small": (9, 13, 1, 1, 1, 3, "ACDEFGHIKLMNORSTUY"),
}


def glyph(ch, width, height, half, cut, narrow):
    w = narrow if ch == "I" else width
    box = Image.new("1", (w, height))
    pen = ImageDraw.Draw(box)
    span_x = w - 1 - 2 * half
    span_y = height - 1 - 2 * half
    for line in SKELETONS[ch]:
        pen.line([(round(half + x * span_x), round(half + y * span_y)) for x, y in line], fill=1)
    bones = np.array(box, dtype=bool)
    out = np.zeros_like(bones)
    for dy in range(-half, half + 1):
        for dx in range(-half, half + 1):
            if abs(dx) + abs(dy) <= 2 * half - cut:
                out |= np.roll(np.roll(bones, dy, 0), dx, 1)
    return out


def runs(picture):
    """Rows of equal spans: [(rows, [(x, length)...])...]."""
    result = []
    for row in picture:
        edges = np.flatnonzero(np.diff(np.concatenate(([0], row.astype(np.int8), [0]))))
        spans = [(int(a), int(b - a)) for a, b in zip(edges[::2], edges[1::2])]
        if result and result[-1][1] == spans and result[-1][0] < 255:
            result[-1] = (result[-1][0] + 1, spans)
        else:
            result.append((1, spans))
    return result


def name_of(ch):
    return "minus" if ch == "-" else ch


def tables():
    """The assembler's tables, and the pictures for the preview."""
    lines = [BEGIN]
    pictures = {}
    size = 0
    for font, (width, height, half, cut, gap, narrow, chars) in FONTS.items():
        lines.append(f"; {font}: {height} rows, a gap of {gap}; a glyph: its width, then runs: rows, spans, x and length of each")
        lines.append(f"{font}_font dw {font}_glyphs, {font}_chars")
        lines.append(f"    db {gap}, {width}")
        lines.append(f"{font}_glyphs:")
        for ch in chars:
            lines.append(f"    dw {font}_{name_of(ch)}")
        lines.append(f"{font}_chars db '{chars}', 0")
        for ch in chars:
            picture = glyph(ch, width, height, half, cut, narrow)
            pictures[font, ch] = picture
            data = [picture.shape[1]]
            for count, spans in runs(picture):
                data += [count, len(spans)] + [v for span in spans for v in span]
            data.append(0)
            size += len(data)
            lines.append(f"{font}_{name_of(ch)}:")
            for i in range(0, len(data), 24):
                lines.append("    db " + ", ".join(str(v) for v in data[i:i + 24]))
    lines.append(END)
    print(f"{size} bytes of lettering")
    return lines, pictures


def rewrite(lines):
    with open(SOURCE, newline="") as f:
        text = f.read().split("\n")
    a, b = text.index(BEGIN), text.index(END)
    text[a:b + 1] = lines
    with open(SOURCE, "w", newline="") as f:
        f.write("\n".join(text))


def text_width(pictures, font, text):
    gap = FONTS[font][4]
    return sum((FONTS[font][0] // 2 if c == " " else pictures[font, c].shape[1]) + gap for c in text) - gap


def put(page, pictures, font, text, x, y, colour):
    for c in text:
        if c == " ":
            x += FONTS[font][0] // 2 + FONTS[font][4]
            continue
        p = pictures[font, c]
        page[y:y + p.shape[0], x:x + p.shape[1]][p] = colour
        x += p.shape[1] + FONTS[font][4]


def page(pictures, weapon, ammo, heat, dist, status, hot=False):
    out = np.zeros((SCREEN_H, SCREEN_W), dtype=np.uint8)
    put(out, pictures, "big", weapon, (SCREEN_W - text_width(pictures, "big", weapon)) // 2, 1, 2)
    for rule in (60, 101, 142):
        out[rule, 2:110] = 1
    if heat is None:
        put(out, pictures, "small", "AMMO", 2, 62, 1)
        if ammo:
            put(out, pictures, "mid", str(ammo).rjust(5), 32, 76, 2)
        else:
            put(out, pictures, "mid", "NO AMMO", 1, 76, 3)
    else:
        put(out, pictures, "small", "HEAT", 2, 62, 1)
        out[76, 2:110] = out[99, 2:110] = 1
        out[76:100, 2] = out[76:100, 109] = 1
        out[78:98, 4:4 + heat * 104 // 100] = 3 if hot else 2
    put(out, pictures, "small", "DIST", 2, 103, 1)
    put(out, pictures, "mid", (str(dist) if dist else "---").rjust(5), 32, 117, 2 if dist else 1)
    if status:
        put(out, pictures, "small", status, 2, 145, 3 if hot else 2)
    return out


def preview(path, pictures):
    pages = [page(pictures, "GUN", 0, 35, 2450, ""),
             page(pictures, "LSR", 0, 96, 812, "TOO HOT", True),
             page(pictures, "MSL", 2, None, 4375, "GUIDING"),
             page(pictures, "IR", 0, None, 0, "SEEKER OFF")]
    gap = 6
    sheet = np.zeros((SCREEN_H, (SCREEN_W + gap) * len(pages) - gap), dtype=np.uint8)
    for i, p in enumerate(pages):
        sheet[:, i * (SCREEN_W + gap):i * (SCREEN_W + gap) + SCREEN_W] = p
    rgb = np.zeros(sheet.shape + (3,), dtype=np.uint8)
    for value, colour in PALETTE.items():
        rgb[sheet == value] = colour
    Image.fromarray(rgb).resize((sheet.shape[1] * 4, sheet.shape[0] * 4), Image.NEAREST).save(path)


def main():
    lines, pictures = tables()
    if len(sys.argv) > 1:
        preview(sys.argv[1], pictures)
    else:
        rewrite(lines)


main()
