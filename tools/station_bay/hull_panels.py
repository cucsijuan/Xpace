# Makes hull panels in the station's own style out of its ring: pieces of the
# outer band's skin (its plates, the ones that stand out, its lit windows),
# flattened, to clad whatever stands out of the station (the hangars' backs)
# or to build new parts of it in the editor.
#
#   blender -b -P tools/station_bay/hull_panels.py
#
# Run it after tools/import_synty.cmake has copied the ring. For each panel
# in PANELS it cuts a piece of SM_Ship_Station_01's band (a slice of its outer
# face: an angle and a height range), unrolls it flat (arc length across,
# height up, radius out) and centres it: in Godot the panel faces +Z, its
# skin at z = 0 with its plates standing out to a few metres, PANEL_W wide
# and PANEL_H high. Writes assets/synty/cut/SM_Station_HullPanel_<name>.glb
# (Synty's mesh: not in the repository) and, ours, assets/stations/bay/
# hull_trim.glb: a 1 m beam to cover panels' edges and corners.

import bpy, math, os, sys
from mathutils import Vector

sys.path.insert(0, os.path.dirname(__file__))
from atlas import CUT, BAY, Builder, import_fbx, prism, cut, export

SKIN_R = 279.0     # the band's outer face, from the station's axis
PANEL_W = 30.0     # m across (along the band)
PANEL_H = 30.0     # m high
# name: (the slice's middle angle, degrees from +X to +Y in Blender's frame;
# its middle height, m)
PANELS = {
    "A": (210.0, -95.0),   # light, plates standing out almost 4 m
    "B": (330.0, -68.0),   # white, from just under the band's top
    "C": (150.0, -122.0),  # blue, from near its bottom
}


def slice_of(angle, middle):
    """A wedge round the slice, from inside the skin to past its plates."""
    half = math.radians(PANEL_W / SKIN_R / 2 * 180 / math.pi)
    a = math.radians(angle)

    def at(r, t, z):
        return Vector((r * math.cos(a + t), r * math.sin(a + t), z))
    ring = [(SKIN_R - 0.6, -half), (SKIN_R + 8, -half), (SKIN_R + 8, half), (SKIN_R - 0.6, half)]
    z0, z1 = middle - PANEL_H / 2, middle + PANEL_H / 2
    return prism("slice", [at(r, t, z0) for r, t in ring], [at(r, t, z1) for r, t in ring])


def unroll(obj, angle, middle):
    """Arc length across, height up, radius out; in Blender's frame for a
    panel that faces Godot's +Z: across = x, up = z, out = -y."""
    a = math.radians(angle)
    for v in obj.data.vertices:
        r = math.hypot(v.co.x, v.co.y)
        t = math.atan2(v.co.y, v.co.x) - a
        t = (t + math.pi) % (2 * math.pi) - math.pi
        # +t along +x keeps the frame right-handed (no mirror: faces stay front side out)
        across, up, out = t * SKIN_R, v.co.z - middle, r - SKIN_R
        v.co = Vector((across, -out, up))
    obj.data.update()


def trim():
    b = Builder()
    s = 0.5
    corners = [Vector((x, y, -s)) for x, y in ((-s, -s), (s, -s), (s, s), (-s, s))]
    tops = [c + Vector((0, 0, 2 * s)) for c in corners]
    b.face(tops, Vector((0, 0, 1)), "dark")
    b.face(list(reversed(corners)), Vector((0, 0, -1)), "dark")
    for k in range(4):
        a, c = corners[k], corners[(k + 1) % 4]
        out = (a + c) / 2
        out.z = 0
        b.face([a, c, tops[(k + 1) % 4], tops[k]], out.normalized(), "steel")
    return b.mesh("HullTrim")


def main():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    for name, (angle, middle) in PANELS.items():
        ring = import_fbx("SM_Ship_Station_01")
        wedge = slice_of(angle, middle)
        cut(ring, [wedge], 'INTERSECT')
        bpy.data.objects.remove(wedge)
        unroll(ring, angle, middle)
        print("panel", name, len(ring.data.polygons), "faces")
        export([ring], os.path.join(CUT, "SM_Station_HullPanel_%s.glb" % name))
        bpy.data.objects.remove(ring)
    export([trim()], os.path.join(BAY, "hull_trim.glb"))


if __name__ == "__main__":
    main()
