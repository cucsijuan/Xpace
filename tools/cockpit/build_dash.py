# Makes room on the fighter's front panel for larger screens, out of Synty's
# SM_Veh_POV_Cockpit_01:
#
#   blender -b -P tools/cockpit/build_dash.py
#
# The panel keeps its shape. Its upper edge goes up, along the slant of its own
# sides, in front of the cowling that already hides the sky there. The housing
# of the weapon and radar screens is remapped, not scaled: their openings grow
# and the bezels round them keep about their width. The frame on the left, where
# the sliders were, goes round the threat receiver's larger screen. Everything
# else stays where it was, so the mesh part tool's boxes still hold.
#
# Measures are in the panel's plane, in metres from where the panel's screens
# used to be centred: u across, v up along the tilt, w out towards the pilot.
# scenes/panels/cockpit and fighter_cockpit.tscn place the screens to match.

import bisect, bmesh, bpy, os, sys

sys.path.insert(0, os.path.join(os.path.dirname(__file__), "..", "station_bay"))
from atlas import SYNTY, CUT, export

SOURCE = os.path.join(SYNTY, "SM_Veh_POV_Cockpit_01.fbx")
OUTPUT = os.path.join(CUT, "SM_Veh_POV_Cockpit_Dash.glb")
MESH = "SM_Veh_POV_Cockpit_01"

ORIGIN = (0, 0.4239, -0.8281)       # in the cockpit scene's frame
UP = (0, 0.951057, -0.309017)
OUT = (0, 0.309017, 0.951057)

PANEL_HALF = 0.8289                 # the half width of the piece the panel is part of
RAISE = 0.05                        # how far the panel's upper edge goes up
# Vertices of the upper edge, by their old v: how much closer to the middle they
# come, so each stays on the line of its slanted side
UPPER_EDGE = ((0.124, 0.0145), (0.113, 0.0145), (0.090, 0.0157), (0.082, 0.0166))

HOUSING_BOX = (-0.011, 0.246)       # the housing's old u, to find it
HOUSING_U = ((-0.0110, -0.0800), (0.0152, -0.0538), (0.0632, 0.0278),
             (0.1100, 0.0538), (0.2200, 0.2408), (0.2460, 0.2548))
HOUSING_V = ((-0.0750, -0.0788), (-0.0470, -0.0553), (-0.0410, -0.0480), (0.0440, 0.0948), (0.0740, 0.1248))

FRAME_BOX = (-0.249, -0.034)        # the left frame's old u
FRAME_U = ((-0.2490, -0.2760), (-0.0340, -0.0820))


def to_panel(co):
    """A vertex of the model, turned half a turn as the cockpit has it, in the panel's measures."""
    p = (-co.x - ORIGIN[0], co.z - ORIGIN[1], co.y - ORIGIN[2])
    return p[0], sum(a * b for a, b in zip(p, UP)), sum(a * b for a, b in zip(p, OUT))


def set_panel(co, u, v, w):
    p = [ORIGIN[0] + u, ORIGIN[1] + v * UP[1] + w * OUT[1], ORIGIN[2] + v * UP[2] + w * OUT[2]]
    co.x, co.z, co.y = -p[0], p[1], p[2]


def remap(knots, value):
    """Piecewise linear through the knots, moved as the nearest knot past the ends."""
    olds = [k[0] for k in knots]
    i = min(max(bisect.bisect_right(olds, value), 1), len(knots) - 1)
    (a, a2), (b, b2) = knots[i - 1], knots[i]
    return a2 + (value - a) * (b2 - a2) / (b - a)


def pieces(bm):
    """The mesh's loose pieces, as lists of vertices."""
    seen, out = set(), []
    for start in bm.verts:
        if start in seen:
            continue
        seen.add(start)
        stack, piece = [start], []
        while stack:
            vert = stack.pop()
            piece.append(vert)
            for edge in vert.link_edges:
                other = edge.other_vert(vert)
                if other not in seen:
                    seen.add(other)
                    stack.append(other)
        out.append(piece)
    return out


def span(piece):
    us = [to_panel(v.co)[0] for v in piece]
    return min(us), max(us)


def main():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=SOURCE)
    obj = next(o for o in bpy.data.objects if o.type == 'MESH')
    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    obj.name = MESH
    obj.data.name = MESH

    bm = bmesh.new()
    bm.from_mesh(obj.data)
    moved = {"edge": 0, "housing": 0, "frame": 0}
    for piece in pieces(bm):
        lo, hi = span(piece)
        if abs(lo - HOUSING_BOX[0]) < 0.003 and abs(hi - HOUSING_BOX[1]) < 0.003:
            for vert in piece:
                u, v, w = to_panel(vert.co)
                set_panel(vert.co, remap(HOUSING_U, u), remap(HOUSING_V, v), w)
                moved["housing"] += 1
        elif abs(lo - FRAME_BOX[0]) < 0.003 and abs(hi - FRAME_BOX[1]) < 0.003:
            for vert in piece:
                u, v, w = to_panel(vert.co)
                set_panel(vert.co, remap(FRAME_U, u), remap(HOUSING_V, v), w)
                moved["frame"] += 1
        elif abs(lo + PANEL_HALF) < 0.003 and abs(hi - PANEL_HALF) < 0.003:
            for vert in piece:
                u, v, w = to_panel(vert.co)
                if abs(u) > 0.33 or not -0.06 < w < 0.03:
                    continue
                for old, inward in UPPER_EDGE:
                    if abs(v - old) < 0.002:
                        side = 0 if abs(u) < 0.1 else (1 if u > 0 else -1)
                        set_panel(vert.co, u - side * inward, v + RAISE, w)
                        moved["edge"] += 1
                        break
    print("moved", moved)
    if moved["edge"] != 12 or not moved["housing"] or not moved["frame"]:
        raise SystemExit("the model is not the one this script was made for")
    for face in bm.faces:
        face.smooth = False
    bm.to_mesh(obj.data)
    bm.free()
    obj.data.update()
    export([obj], OUTPUT)


main()
