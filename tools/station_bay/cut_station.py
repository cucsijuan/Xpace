# Cuts the big hangars' rooms out of the station's ring.
#
#   blender -b -P tools/station_bay/cut_station.py
#
# Run it after build_hangar.py (it reads the hangar it made). The hangar goes
# through the ring's outer band (SM_Ship_Station_01: 226.5 to 279.5 m from
# the axis, -50 to -140 m high) with its door in the band's outer face and
# its back standing clear in the ring's hollow. There are four, midway
# between the ring's arms (every 90 degrees), 45 degrees either side of the
# spacewalk tubes and opposite them; the missile turrets on the band stand
# well above them.
#
# Writes assets/synty/cut/SM_Ship_Station_01_Hangar.glb and
# SM_Ship_Station_01_Collision_Hangar.glb (Synty's meshes: not in the
# repository), and prints the hangars' transforms for the station's scene
# (scenes/stations/synty_station.tscn, nodes HangarBig1 to 4): keep the two
# the same.

import bpy, math, os, sys
from mathutils import Vector

sys.path.insert(0, os.path.dirname(__file__))
from atlas import CUT, import_fbx, prism, cut, export
from build_hangar import POCKET

ANGLES = (45.0, 135.0, 225.0, 315.0)  # about the station's axis, from +X towards -Z (Godot's frame)
DOOR_R = 280.0    # the door wall's outer face, from the axis
FLOOR = -112.0    # the hangar's floor
MARGIN = 0.1      # the cut is this much bigger than the hangar
OUTWARD = 3.0     # and reaches this far past the door, through the band's skin
POCKETS = ANGLES    # the hangars that get a slot for their raised door


def hangar_bounds():
    """The hangar's room in its own Godot frame: (low, high) per axis."""
    before = set(bpy.data.objects)
    bpy.ops.import_scene.gltf(filepath=os.path.join(CUT, "SM_Bld_Hangar_Big.glb"))
    new = [o for o in bpy.data.objects if o not in before]
    bpy.context.view_layer.update()
    vs = [o.matrix_world @ v.co for o in new if o.type == 'MESH' for v in o.data.vertices]
    for o in new:
        bpy.data.objects.remove(o)
    # Blender (x, y, z) is Godot (x, z, -y)
    g = [(v.x, v.z, -v.y) for v in vs]
    return [(min(p[i] for p in g), max(p[i] for p in g)) for i in range(3)]


def placement(angle, door_z):
    """A hangar's basis columns and origin in the station's Godot frame: its
    +Z (the door's side) points out along the ring's radius."""
    a = math.radians(angle)
    out = Vector((math.cos(a), 0, -math.sin(a)))
    up = Vector((0, 1, 0))
    side = up.cross(out)
    origin = out * (DOOR_R - door_z) + up * FLOOR
    return side, up, out, origin


def to_blender(p):
    return Vector((p.x, -p.z, p.y))


def main():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    (x0, x1), (y0, y1), (z0, z1) = hangar_bounds()
    m = MARGIN
    rooms, lines = [], []
    for n, angle in enumerate(ANGLES, 1):
        side, up, out, origin = placement(angle, z1)

        def at(x, y, z):
            return to_blender(origin + side * x + up * y + out * z)
        corners = ((x0 - m, z0 - m), (x1 + m, z0 - m), (x1 + m, z1 + OUTWARD), (x0 - m, z1 + OUTWARD))
        rooms.append(prism("room%d" % n, [at(x, y0 - m, z) for x, z in corners],
                           [at(x, y1 + m, z) for x, z in corners]))
        # The slot the raised door slides up into, open out of the hull: the
        # band's plates that stand out a little no longer go through the door
        if angle in POCKETS:
            # Not quite flush with the room's cut: coplanar faces stall the cut
            hw, high, behind = POCKET
            front = z1 + OUTWARD + 0.13
            slot = ((-hw, behind), (hw, behind), (hw, front), (-hw, front))
            pocket = prism("pocket%d" % n, [at(x, y1 - 0.37, z) for x, z in slot],
                           [at(x, high, z) for x, z in slot])
            # One cutter per hangar: many exact cuts of the ring in a row stall
            cut(rooms[-1], [pocket], 'UNION')
            bpy.data.objects.remove(pocket)
        rows = [(side.x, up.x, out.x), (side.y, up.y, out.y), (side.z, up.z, out.z)]
        lines.append("HangarBig%d transform = Transform3D(%s)" % (n, ", ".join(
            "%.6g" % v for v in [*rows[0], *rows[1], *rows[2], origin.x, origin.y, origin.z])))
    for name in ("SM_Ship_Station_01", "SM_Ship_Station_01_Collision"):
        ring = import_fbx(name)
        before = len(ring.data.polygons)
        cut(ring, rooms)
        print(name, before, "->", len(ring.data.polygons), "faces")
        export([ring], os.path.join(CUT, name + "_Hangar.glb"))
        bpy.data.objects.remove(ring)
    for line in lines:
        print(line)


if __name__ == "__main__":
    main()
