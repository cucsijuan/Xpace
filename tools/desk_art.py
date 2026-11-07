#!/usr/bin/env python3
# Makes DeskPilot's lettering and the icons of the ship's applications: letters
# of varying width, 16 rows high, cut from a bold sans at one bit a pixel, and
# icons of 32 x 32 in the COMMS screen's four colours.
#
#   python3 tools/desk_art.py          # rewrites font.h, icons.h and COMMS's icon
#   python3 tools/desk_art.py out.png  # and draws them, 6 x real size
#
# Needs Pillow, and fc-match to find the font.

import os
import subprocess
import sys
from PIL import Image, ImageDraw, ImageFont

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))
DESK = os.path.join(ROOT, "software", "desk")
COMMS = os.path.join(ROOT, "software", "comms", "comms.asm")
BEGIN = "; ---- the icon (tools/desk_art.py) ----"
END = "; ---- end of the icon ----"

FONT = "Liberation Sans:bold"          # a plain bold sans, as the desktops of the day had
FONT_SIZE = 15
CELL_H = 16
BASELINE = 13
MOST_WIDE = 16

BLACK, TEAL, NAVY, WHITE = 0, 1, 2, 3
COLOURS = {0: (0, 0, 0), 1: (0, 128, 128), 2: (0, 0, 128), 3: (255, 255, 255)}

def glyphs():
    """Each printable character: its width and rows of 16 bits, the leftmost pixel in the highest."""
    path = subprocess.check_output(["fc-match", "-f", "%{file}", FONT], text=True)
    font = ImageFont.truetype(path, FONT_SIZE)
    out = []
    for code in range(32, 127):
        ch = chr(code)
        image = Image.new("1", (MOST_WIDE + 8, CELL_H + 8), 0)
        d = ImageDraw.Draw(image)
        d.fontmode = "1"
        d.text((0, BASELINE), ch, font=font, fill=1, anchor="ls")
        width = min(MOST_WIDE, max(int(round(font.getlength(ch))), 3))
        rows = [sum(bool(image.getpixel((x, y))) << (15 - x) for x in range(MOST_WIDE)) for y in range(CELL_H)]
        out.append((width, rows))
    return out


def icon_comms():
    i = Image.new("P", (32, 32), WHITE)
    i.putpalette(sum((list(COLOURS[k]) for k in range(4)), []))
    d = ImageDraw.Draw(i)
    d.polygon([(16, 12), (7, 30), (10, 30), (16, 18), (22, 30), (25, 30)], fill=NAVY)
    d.line([(10, 25), (22, 25)], fill=NAVY, width=2)
    d.ellipse([12, 7, 20, 15], fill=BLACK)
    d.ellipse([14, 9, 18, 13], fill=TEAL)
    for r, colour in ((9, TEAL), (13, NAVY)):
        d.arc([16 - r, 11 - r, 16 + r, 11 + r], -50, 50, fill=colour, width=2)
        d.arc([16 - r, 11 - r, 16 + r, 11 + r], 130, 230, fill=colour, width=2)
    return i


def icon_scanner():
    i = Image.new("P", (32, 32), WHITE)
    i.putpalette(sum((list(COLOURS[k]) for k in range(4)), []))
    d = ImageDraw.Draw(i)
    d.rectangle([7, 10, 24, 25], fill=TEAL, outline=BLACK, width=2)
    d.line([(7, 15), (24, 15)], fill=BLACK, width=1)
    d.line([(15, 10), (15, 15)], fill=BLACK, width=1)
    d.rectangle([13, 18, 18, 21], fill=WHITE, outline=BLACK)
    for x, y, dx, dy in ((1, 1, 1, 1), (30, 1, -1, 1), (1, 30, 1, -1), (30, 30, -1, -1)):
        d.line([(x, y), (x + 6 * dx, y)], fill=NAVY, width=2)
        d.line([(x, y), (x, y + 6 * dy)], fill=NAVY, width=2)
    return i


def logo():
    """A screen on its stand, with a ship climbing across it, on the splash's navy."""
    i = Image.new("P", (64, 64), NAVY)
    i.putpalette(sum((list(COLOURS[k]) for k in range(4)), []))
    d = ImageDraw.Draw(i)
    d.rounded_rectangle([6, 4, 57, 45], 6, fill=WHITE)
    d.rounded_rectangle([11, 9, 52, 40], 3, fill=TEAL)
    d.rectangle([26, 46, 37, 52], fill=WHITE)
    d.rounded_rectangle([16, 53, 47, 58], 2, fill=WHITE)
    d.polygon([(46, 13), (18, 26), (29, 28), (32, 37)], fill=WHITE)
    d.polygon([(46, 13), (29, 28), (32, 37)], fill=BLACK)
    d.line([(14, 37), (22, 33)], fill=WHITE, width=2)
    d.line([(20, 38), (25, 35)], fill=WHITE, width=1)
    return i


def packed(icon):
    """Rows of 2 bits a pixel, the leftmost in the highest bits."""
    out = []
    for y in range(icon.height):
        for x in range(0, icon.width, 4):
            out.append(sum(icon.getpixel((x + k, y)) << ((3 - k) * 2) for k in range(4)))
    return out


def rows_of(values, width):
    return [", ".join(str(v) for v in values[i:i + width]) for i in range(0, len(values), width)]


def main():
    letters = glyphs()
    with open(os.path.join(DESK, "font.h"), "w", newline="\n") as f:
        f.write("/* font.h - DeskPilot's letters (tools/desk_art.py): characters 32 to 126, 16 rows of 16 bits, and their widths */\n")
        f.write(f"#define GLYPH_ROWS {CELL_H}\n\nunsigned font[] = {{\n")
        for _, rows in letters:
            f.write("    " + ", ".join(hex(r) for r in rows) + ",\n")
        f.write("};\n\nchar widths[] = {\n")
        for line in rows_of([w for w, _ in letters], 16):
            f.write("    " + line + ",\n")
        f.write("};\n")
    icons = {"comms": icon_comms(), "scanner": icon_scanner()}
    with open(os.path.join(DESK, "icons.h"), "w", newline="\n") as f:
        f.write("/* icons.h - the applications' icons (tools/desk_art.py): 32 x 32, 2 bits a pixel */\n\n")
        f.write("char icon_scanner[] = {\n")
        for line in rows_of(packed(icons["scanner"]), 16):
            f.write("    " + line + ",\n")
        f.write("};\n")
    with open(os.path.join(DESK, "logo.h"), "w", newline="\n") as f:
        f.write("/* logo.h - DeskPilot's logo (tools/desk_art.py): 64 x 64, 2 bits a pixel */\n\n")
        f.write("char logo[] = {\n")
        for line in rows_of(packed(logo()), 16):
            f.write("    " + line + ",\n")
        f.write("};\n")
    text = open(COMMS).read()
    table = "\n".join("            db " + line for line in rows_of(packed(icons["comms"]), 16))
    begin, end = text.index(BEGIN) + len(BEGIN), text.index(END)
    open(COMMS, "w", newline="\n").write(text[:begin] + "\nicon\n" + table + "\n" + text[end:])
    if len(sys.argv) > 1:
        sheet = Image.new("RGB", (256, 120), (255, 255, 255))
        x, y = 4, 4
        for width, rows in letters:
            if x + width > 250:
                x, y = 4, y + 18
            for r, row in enumerate(rows):
                for c in range(MOST_WIDE):
                    if row >> (15 - c) & 1:
                        sheet.putpixel((x + c, y + r), (0, 0, 0))
            x += width
        for n, icon in enumerate(icons.values()):
            sheet.paste(icon.convert("RGB"), (8 + n * 40, 60))
        sheet.paste(logo().convert("RGB"), (100, 50))
        sheet.resize((256 * 6, 120 * 6), Image.NEAREST).save(sys.argv[1])


main()
