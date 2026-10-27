# Makes a hangar big enough for every ship short of a capital one out of
# Synty's SM_Bld_Hangar_01 (40 x 31 m, a 26.8 x 11 m door: fighters only).
#
#   blender -b -P tools/station_bay/build_hangar.py
#
# The room is built bigger the way it was built: its walls are repeated, not
# stretched. Along the room the side walls repeat a bay (a rib and its panel,
# 3.75 m: they are laid out on that grid); across it a 7 m length of the door
# wall, the door's halves and the control room repeats, so the door and the
# control room's window grow; up, the walls grow through a band where they
# are plain (no vertex in it), so nothing there is stretched but bare wall.
# Synty's floor, with its pit, is left out: the hangar gets a flat floor of
# its own (central_hangar.glb, built here too). Its collision meshes get the
# same treatment.
#
# Writes assets/synty/cut/SM_Bld_Hangar_Big.glb (the room's meshes, by name:
# the door halves and the glass apart) and SM_Bld_Hangar_Big_Collision.glb
# (Synty's meshes: not in the repository), and assets/stations/bay/
# hangar_floor.glb (ours). Blender's frame: Z up, the door wall at -Y; Godot's
# is (x, z, -y).

import bpy, bmesh, os, sys
from mathutils import Vector

sys.path.insert(0, os.path.dirname(__file__))
from atlas import SYNTY, CUT, BAY, Builder, export

ALONG = (-7.44, -3.69, 18)   # a side walls' bay (y from, to) and how many more: 98.5 m long
ACROSS = (-8.4, -4.5, 7)     # a length of the door wall through the door (x from, to) and how many more: 67 m wide, a 54 m door
BACK_SEAMS = (-17.35, 18.66)  # the back wall widens at these instead, beside its corners, so the control room
                              # and the small doors keep their size
BACK_SET = (-13.85, 18.6)    # the small door, the control room and the cargo door: kept in the middle
SMALL_ACROSS = 10.0          # at the back wall's seams, parts narrower than this (a rib, a catwalk, a rail)
                             # are not stretched: they stay, unless they lie past the seam (at the right one,
                             # past CORNER short of it: the ribs and catwalk in the corner go with the side wall)
CORNER = 2.5
BACK_WALL = 14.0             # parts lying beyond this (y) are on the back wall
UP = (10.1, 11.0)            # where the walls are plain (z) and how much higher: a 22 m door
SMALL = 3.0                  # loose parts shorter than this (rails, pipes) are moved whole, not stretched
SMALL_UP = 12.1              # and moved up only if they stand above this (the rail on the control room's roof
                             # reaches over UP's seam; its handrail is a part of its own)
WELD = 0.002
# The pocket the raised door slides into, above the door (Godot frame: half
# width, top, back; it opens out of the hull, see cut_station.py)
POCKET = (27.6, 45.2, 14.3)
POCKET_DEPTH = 2.7


def import_all(name):
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=os.path.join(SYNTY, name + ".fbx"))
    new = [o for o in bpy.data.objects if o not in before]
    meshes = [o for o in new if o.type == 'MESH']
    bpy.ops.object.select_all(action='DESELECT')
    for o in meshes:
        o.select_set(True)
    bpy.context.view_layer.objects.active = meshes[0]
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    for o in new:
        if o.type != 'MESH':
            bpy.data.objects.remove(o)
    return meshes


def repeat(obj, axis, start, end, more, region=None):
    """Cuts the mesh at start and end along axis and puts more copies of what
    lies between them after it, moving what lies beyond along. With a region
    (a test of a face's middle), only the faces in it."""
    period = end - start
    normal = Vector((0, 0, 0))
    normal[axis] = 1
    bm = bmesh.new()
    bm.from_mesh(obj.data)

    def mine():
        return [f for f in bm.faces if region is None or region(f.calc_center_median())]
    for at in (start, end):
        point = Vector((0, 0, 0))
        point[axis] = at
        faces = mine()
        edges = list({e for f in faces for e in f.edges})
        verts = list({v for f in faces for v in f.verts})
        bmesh.ops.bisect_plane(bm, geom=verts + edges + faces, dist=1e-5, plane_co=point, plane_no=normal)
    slab, beyond = [], []
    for f in mine():
        c = f.calc_center_median()[axis]
        if start < c < end:
            slab.append(f)
        elif c >= end:
            beyond.append(f)
    if beyond:
        moved = bmesh.ops.duplicate(bm, geom=beyond)["geom"]
        bmesh.ops.translate(bm, verts=[v for v in moved if isinstance(v, bmesh.types.BMVert)],
                            vec=normal * (period * more))
        bmesh.ops.delete(bm, geom=beyond, context='FACES')
    for k in range(1, more + 1):
        copy = bmesh.ops.duplicate(bm, geom=slab)["geom"]
        bmesh.ops.translate(bm, verts=[v for v in copy if isinstance(v, bmesh.types.BMVert)],
                            vec=normal * (period * k))
    bmesh.ops.remove_doubles(bm, verts=bm.verts[:], dist=WELD)
    bm.to_mesh(obj.data)
    bm.free()
    obj.data.update()


def parts(mesh):
    """Each vertex's loose part, as the part's bounds (low, high) per axis."""
    parent = list(range(len(mesh.vertices)))

    def root(i):
        while parent[i] != i:
            parent[i] = parent[parent[i]]
            i = parent[i]
        return i
    for e in mesh.edges:
        a, b = root(e.vertices[0]), root(e.vertices[1])
        if a != b:
            parent[a] = b
    low, high = {}, {}
    for v in mesh.vertices:
        r = root(v.index)
        low[r] = [min(a, b) for a, b in zip(low.get(r, v.co), v.co)]
        high[r] = [max(a, b) for a, b in zip(high.get(r, v.co), v.co)]
    return [(low[root(i)], high[root(i)]) for i in range(len(mesh.vertices))]


def seam(obj, axis, at, more, small, small_at, only=None):
    """Moves what lies past at by more. A loose part for which small(low,
    high) holds (a rail, a pipe) is not stretched: it moves whole if it starts
    past small_at, or stays. only: the vertices to move (default all)."""
    bounds = parts(obj.data)
    for v in obj.data.vertices:
        if only is not None and v.index not in only:
            continue
        low, high = bounds[v.index]
        if small(low, high):
            if low[axis] > small_at:
                v.co[axis] += more
        elif v.co[axis] > at:
            v.co[axis] += more
    obj.data.update()


def across_small(low, high):
    # Ribs (narrow) and rails or catwalks (low), not lengths of wall nor the
    # trims along the back wall itself (they run on to the control room)
    wide, tall = high[0] - low[0], high[2] - low[2]
    if low[1] > BACK_WALL:
        return False
    return wide < 3.0 or (wide < SMALL_ACROSS and tall < 2.0)


def widen(obj, axis, at, more, region, reach):
    """A seam in the region only; ribs, rails and catwalks move whole (reach:
    if they start within CORNER of at; else only if they lie wholly past it)."""
    seam(obj, axis, at, more, across_small, at - CORNER if reach else at,
         {i for p in obj.data.polygons if region(p.center) for i in p.vertices})


def raise_walls(obj, at, more):
    """Moves up what is above at; small parts only from SMALL_UP up, so the
    control room's rail stays on its roof."""
    seam(obj, 2, at, more, lambda low, high: high[2] - low[2] < SMALL, SMALL_UP)


def shift(obj, offset):
    for v in obj.data.vertices:
        v.co += offset
    obj.data.update()


def bounds(objs):
    vs = [v.co for o in objs for v in o.data.vertices]
    return [(min(v[i] for v in vs), max(v[i] for v in vs)) for i in range(3)]


def main():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    # Synty's floor (with its pit) and ceiling give way to ours: a ceiling
    # cannot be repeated through a door and widened at the back alike
    room = [o for o in import_all("SM_Bld_Hangar_01") if "HangerFloor" not in o.name and "Ceiling" not in o.name]
    for o in bpy.data.objects:
        if o.type == 'MESH' and ("HangerFloor" in o.name or "Ceiling" in o.name):
            bpy.data.objects.remove(o)
    walls = import_all("SM_Bld_Hanger_01_Collision")
    everything = room + walls
    wider = (ACROSS[1] - ACROSS[0]) * ACROSS[2]

    def front(c):
        return c.y < 0

    def back(c):
        return c.y >= 0
    for o in everything:
        # Across first, while the room is its own size: the front half through
        # the door, the back half at its plain seam, by as much
        repeat(o, 0, ACROSS[0], ACROSS[1], ACROSS[2], front)
        # The set ends up in the middle once the room is centred
        left = wider / 2 - (BACK_SET[0] + BACK_SET[1]) / 2
        widen(o, 0, BACK_SEAMS[1], wider - left, back, True)
        widen(o, 0, BACK_SEAMS[0], left, back, False)
        repeat(o, 1, ALONG[0], ALONG[1], ALONG[2])
        raise_walls(o, UP[0], UP[1])
        # Keep the room centred across, with its door wall where it was
        shift(o, Vector((-(ACROSS[1] - ACROSS[0]) * ACROSS[2] / 2, 0, 0)))
    (x0, x1), (y0, y1), (z0, z1) = bounds(room)
    print("hangar x %.1f..%.1f  y %.1f..%.1f  z %.1f..%.1f" % (x0, x1, y0, y1, z0, z1))

    for o in room:
        o.name = o.name.replace("Hanger", "Hangar")
    export(room, os.path.join(CUT, "SM_Bld_Hangar_Big.glb"))
    export(walls, os.path.join(CUT, "SM_Bld_Hangar_Big_Collision.glb"))

    b = Builder()
    up = Vector((0, 0, 1))
    inner = 18.7 + wider / 2   # the walls' foot, inside
    front, back = -14.9, 15.0 + (ALONG[1] - ALONG[0]) * ALONG[2]
    # A diamond-plate floor with a hazard border, laid in panels
    b.quad(Vector((-inner + 1.5, front + 1.5, 0)), Vector((inner - 1.5, front + 1.5, 0)),
           Vector((inner - 1.5, back - 1.5, 0)), Vector((-inner + 1.5, back - 1.5, 0)), up, "diamond",
           (round((2 * inner) / 8), round((back - front) / 8)))
    for a, c in (((-inner, front), (inner, front + 1.5)), ((-inner, back - 1.5), (inner, back)),
                 ((-inner, front + 1.5), (-inner + 1.5, back - 1.5)), ((inner - 1.5, front + 1.5), (inner, back - 1.5))):
        b.quad(Vector((a[0], a[1], 0)), Vector((c[0], a[1], 0)), Vector((c[0], c[1], 0)), Vector((a[0], c[1], 0)),
               up, "hazard", (max(1, round(abs(c[0] - a[0]) / 6)), max(1, round(abs(c[1] - a[1]) / 6))))
    # The ceiling: plates between beams across the room, a strip of light
    # under each beam
    down = Vector((0, 0, -1))
    top = 12.3 + UP[1]
    span = 18.9 + wider / 2
    b.quad(Vector((-span, front, top)), Vector((span, front, top)), Vector((span, back, top)),
           Vector((-span, back, top)), down, "panel", (round(2 * span / 9), round((back - front) / 9)))
    y = front + 6.0
    while y < back - 3.0:
        for side in (-0.6, 0.6):
            b.quad(Vector((-span, y + side, top - 1.6)), Vector((span, y + side, top - 1.6)), Vector((span, y + side, top)),
                   Vector((-span, y + side, top)), Vector((0, side, 0)), "steel", (round(2 * span / 12), 1))
        b.quad(Vector((-span, y - 0.6, top - 1.6)), Vector((span, y - 0.6, top - 1.6)), Vector((span, y + 0.6, top - 1.6)),
               Vector((-span, y + 0.6, top - 1.6)), down, "grey")
        b.quad(Vector((-span + 3, y - 0.2, top - 1.62)), Vector((span - 3, y - 0.2, top - 1.62)),
               Vector((span - 3, y + 0.2, top - 1.62)), Vector((-span + 3, y + 0.2, top - 1.62)), down, "glow_white")
        y += 7.5
    # Outside: a roof and an underside, for where the room stands clear of the
    # station's hull
    b.quad(Vector((x0, y0, z1)), Vector((x1, y0, z1)), Vector((x1, y1, z1)), Vector((x0, y1, z1)), up, "steel",
           (round((x1 - x0) / 12), round((y1 - y0) / 12)))
    b.quad(Vector((x0, y0, z0)), Vector((x1, y0, z0)), Vector((x1, y1, z0)), Vector((x0, y1, z0)), down, "steel",
           (round((x1 - x0) / 12), round((y1 - y0) / 12)))
    # The door's pocket: a lining for the slot cut above the door in the
    # station's hull, so it shows plates, not the hull's inside, when the door
    # is down
    hw, high, behind = POCKET
    yb, yf = -behind, -behind - POCKET_DEPTH
    b.quad(Vector((-hw, yb, z1)), Vector((hw, yb, z1)), Vector((hw, yb, high)), Vector((-hw, yb, high)),
           Vector((0, -1, 0)), "steel", (round(2 * hw / 9), 2))
    b.quad(Vector((-hw, yb, high)), Vector((hw, yb, high)), Vector((hw, yf, high)), Vector((-hw, yf, high)),
           down, "steel", (round(2 * hw / 9), 1))
    for sx in (-1, 1):
        b.quad(Vector((sx * hw, yb, z1)), Vector((sx * hw, yf, z1)), Vector((sx * hw, yf, high)),
               Vector((sx * hw, yb, high)), Vector((-sx, 0, 0)), "steel", (1, 2))
    floor = b.mesh("HangarFloor")
    export([floor], os.path.join(BAY, "hangar_floor.glb"))


if __name__ == "__main__":
    main()
