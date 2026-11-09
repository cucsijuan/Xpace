"""Redhold, the pirates' sphere station, in Synty's low-poly style:

    blender -b -P tools/pirate_base/build_redhold.py

Builds the station's meshes and writes assets/stations/redhold/redhold.glb (what is
seen: the hull and everything on it, the two door leaves, the guide lights) and
redhold_collision.glb (what ships and walkers meet: all but the doors, which have
their own blocker, and the guide lights). Its faces point at swatches of Synty's atlas
(PolygonSciFiSpace_Texture_01_A and Emissive_01); the glbs hold no texture, the
scene paints them (assets/materials/redhold.tres). It also prints where the
turrets go, in Godot's frame, for scenes/stations/redhold.tscn.

Everything is in metres; in Blender the hangar's door faces -Y and +Z is up, so in
Godot the door faces +Z. Re-running in an open Blender replaces the objects.
"""
import math
import os
import random
import bmesh
import bpy
from mathutils import Matrix, Vector

try:
    ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
except NameError:                     # sent to a running Blender: no file
    ROOT = os.environ.get("XPACE_ROOT", os.getcwd())
OUT = os.path.join(ROOT, "assets", "stations", "redhold")

# Hull
RADIUS = 150.0
SEGMENTS = 32          # around
RINGS = 24             # pole to pole (even: an edge loop on the equator)
BELT_RINGS = 1         # rings either side of the equator raised into the belt
BELT_OUT = 6.0         # how far the belt stands off the hull

# Hangar room (inside)
W, H, D = 72.0, 36.0, 190.0
ROOM_FRONT = -120.0    # y of the room's door end
FLOOR = -H / 2

# Portal round the mouth
FRAME = 8.0            # frame width round the opening
CHAMFER = 12.0         # top corners cut at 45 degrees
PORTAL_FRONT = -164.0  # y of the portal's front face
PORTAL_BACK = -128.0   # y where the portal's sides end, inside the hull
HOOD_OUT = 10.0        # the hood over the door stands out this much more
LIP_OUT = 22.0         # the deck under the door reaches this far out
LIP_THICK = 3.0
BEZEL = (2.5, 2.0)         # black bezel round the frame's front: width, how far it stands out
TUNNEL_RIBS = 6.0          # spacing of the ribs lining the tunnel to the doors
PISTONS = (-6.0, 12.0)     # heights of the hydraulic rams each side of the frame

# Doors: two leaves sliding apart into the hull, behind a tunnel
DOOR_Y = -122.0            # the leaves' middle plane (the tunnel runs from PORTAL_FRONT to here)
DOOR_THICK = 2.0
DOOR_OVERLAP = 1.0         # the leaves reach this far into the walls, floor and ceiling
DOOR_SLIDE = W / 2 + 4.0   # how far each leaf travels to open

# Control tower on the pole (radius, top z of each stage; octagons)
TOWER_SIDES = 8
PLINTH = (27.0, 157.0)     # flared base, its foot sunk into the hull
SHAFT = (13.0,)            # radius at the top; it runs up to the gallery
SHAFT_FOOT = 17.5          # the column widens down to this (the plinth's top is 20.25)
GALLERY = (19.0, 182.0, 2.5)   # radius, top, thickness: the walkway under the cab
CAB_FOOT = (15.5, 183.0)
CAB_TOP = (21.0, 196.0)    # windows lean out, as at an airport
ROOF = (22.5, 199.5)
CAP = (12.0, 204.0)

# Antennas: (x, y, height, thickness) on the roof; the mast is the first
ROOF_MASTS = [(0.0, 0.0, 48.0, 1.4), (12.0, 6.0, 22.0, 0.7), (-11.0, 8.0, 16.0, 0.7),
              (-6.0, -13.0, 26.0, 0.7), (9.0, -10.0, 13.0, 0.6)]
# masts on the hull: (azimuth deg, latitude deg, height, thickness)
HULL_MASTS = [(20, 52, 30, 0.9), (75, 40, 22, 0.8), (140, 55, 34, 0.9),
              (200, 45, 24, 0.8), (255, 58, 28, 0.9), (320, 42, 20, 0.8),
              (10, -50, 26, 0.8), (70, -62, 32, 0.9), (280, -48, 22, 0.8), (330, -66, 28, 0.9)]
TIP = 1.6                  # red light block at each tip

# Dish set into the hull, a feed antenna in its middle
DISH_AT = (-55.0, 32.0)    # azimuth, latitude (deg); the door is at -90, 0
DISH_SIDES = 16
DISH_R = 40.0              # bowl's radius at the rim
DISH_DEPTH = 13.0          # how deep the bowl's middle sits below the rim
DISH_RINGS = 4             # rings of facets from the middle to the rim
RIM = (5.0, 3.0, 16.0)     # rim's width, height over the hull, skirt down into it
# feed antenna: a katana-like section (spine flat, widest at the ridge, sharp edge)
FEED_SECTION = [(-4.8, -1.05), (1.5, -2.25), (5.7, 0.0), (1.5, 2.25), (-4.8, 1.05)]
FEED_BASE = (1.7, 5.0)     # base: the section scaled this much, this tall
FEED_BODY = 30.0           # body's height over the base
FEED_CHAMFER = 4.0         # the top is cut lower at the edge than at the spine
FEED_SPINE_UP = True       # spine faces the pole
# a hollow black sleeve out of the body's cut top, its mouth cut at a slant too
SLEEVE_INSET = 0.6         # narrower than the body by this all round
SLEEVE_LEN = 12.0          # at the spine
SLEEVE_CHAMFER = 5.0       # its mouth's slant (spine to edge), over the body's
SLEEVE_WALL = 0.6
# thin antennas out of the sleeve's floor: (along x, across y, length, thickness, light)
SLEEVE_RODS = [(-1.6, 0.0, 22.0, 0.45, True), (1.4, 0.0, 13.0, 0.35, False)]

# Hardpoints: emplacements for Synty's turrets (Double_06 at x7.09, Double_08 at x9.67:
# 11-14 m bases, 16-20 m with the barrels). (azimuth, latitude) in degrees; lat 0 = on the belt
HARDPOINTS = [(-40, 0), (0, 0), (40, 0), (80, 0), (120, 0), (160, 0), (196, 0), (228, 0),
              (105, 30), (170, 30), (230, 30),
              (-30, -40), (60, -40), (150, -40), (240, -40)]
HP_FOOT = (18.0, 15.5, 6.0, 5.0)   # foot's radius at the hull and at its top, sunk, standing out
HP_DECK = (13.0, 1.0)              # deck's radius and thickness, the turret's seat on top
HP_LIP = 0.8                       # red lip round the deck
HP_COLLAR = 1.5                    # red collar under the deck
HP_SEED = 7

# Hull relief: panels raised or sunk, grown from single facets along their neighbours
RELIEF_SEED = 11
RELIEF_SHARE = 0.4         # share of the free facets that start a panel
RELIEF_GROW = [0, 0, 1, 1, 2]  # extra facets a panel takes (picked at random)
RELIEF_INSET = 1.6         # gap round each panel
RELIEF_RAISE = (0.8, 2.4)  # raised panels stand out this much
RELIEF_SINK = (0.8, 1.6)   # sunk panels go in this much
RELIEF_RAISED = 0.6        # share of panels raised (the rest are sunk)
RELIEF_RED = 0.3           # share of raised panels painted red; the rest are plain black plates
RELIEF_CLEAR = 8.0         # metres kept clear round the mouth, dish, tower and emplacements
RELIEF_DETAIL = 0.75       # share of sunk panels given a vent, pipes or a hatch

# Bolted-on modules: (kind, azimuth, latitude); the keel always hangs from the south pole
MODULES = [("tanks", 200, -52), ("containers", 100, -22), ("habitat", 40, 50), ("feed", 130, -60)]
MOD_SEED = 5
MOD_CLEAR = 30.0           # masts are kept this far from a module

# Synty atlas (PolygonSciFiSpace_Texture_01_A + Emissive_01): (u, v) swatches, v from the image's top
ATLAS = os.path.join(ROOT, "assets", "synty", "PolygonSciFiSpace_Texture_01_A.png")
EMISSIVE = os.path.join(ROOT, "assets", "synty", "PolygonSciFiSpace_Emissive_01.png")
SWATCH = {
    "hull": (0.415, 0.600),      # 3e3e41, the palette's darkest neutral grey
    "hull_light": (0.415, 0.580),  # 555558
    "trim": (0.138, 0.580),      # 973426, palette red
    "trim_dark": (0.138, 0.600),  # 7c271f
    "black": (0.0645, 0.9834),   # 151515
    "black_soft": (0.0791, 0.7637),  # 1b1c1e
    "glass": (0.2969, 0.970),    # emissive orange
    "light": (0.4219, 0.970),    # emissive red
    "room": (0.2656, 0.986),     # 7f7f7f
}
PANEL = (0.480, 0.740, 0.380, 0.965)   # the dark tech panel: u from, u to, v from, v to
PANEL_METRES = 90.0                    # the panel's width covers this many metres of hull
GLOW_TINT = (1.0, 0.06, 0.04)          # the atlas's cyan lights turn this colour (red, orange stay)
PANEL_MIN_AREA = 60.0                  # hull faces this big get the panel; smaller ones a flat swatch
BELT_PANEL = (0.180, 0.495, 0.085, 0.305)   # Synty's big light grey hull panels, for the belt
BELT_TINT = 0.22                       # that region is darkened this much (multiplied)

GENERATED = ("Hull", "Portal", "HangarInterior", "Tower", "Antennas", "Dish", "Hardpoints", "Modules", "DoorLeft", "DoorRight", "GuideLights")
HULL, TRIM, GLASS, LIGHT, PLAIN = range(5)   # PLAIN: flat black plating


def new_object(name, bm, material):
    if name in bpy.data.objects:
        bpy.data.objects.remove(bpy.data.objects[name])
    me = bpy.data.meshes.new(name)
    bm.normal_update()
    for f in bm.faces:
        f.smooth = False
    bm.to_mesh(me)
    bm.free()
    for m in (material if isinstance(material, list) else [material]):
        me.materials.append(m)
    ob = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(ob)
    return ob


def material(name, colour, glow=0.0):
    m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    m.use_nodes = True
    m.diffuse_color = colour
    bsdf = m.node_tree.nodes["Principled BSDF"]
    bsdf.inputs["Base Color"].default_value = colour
    bsdf.inputs["Emission Color"].default_value = colour
    bsdf.inputs["Emission Strength"].default_value = glow
    return m


def octagon(r, sides=None):
    n = sides or TOWER_SIDES
    turn = math.pi / n          # a flat side faces the door
    return [(r * math.cos(turn + 2 * math.pi * k / n), r * math.sin(turn + 2 * math.pi * k / n))
            for k in range(n)]


def stage(bm, r0, z0, r1, z1, side=HULL, bottom=True, top=True, cap_mat=None, sides=None):
    """A frustum between two regular polygons, centred on (0, 0)."""
    a = [bm.verts.new((x, y, z0)) for x, y in octagon(r0, sides)]
    b = [bm.verts.new((x, y, z1)) for x, y in octagon(r1, sides)]
    n = len(a)
    for k in range(n):
        m = (k + 1) % n
        bm.faces.new((a[k], a[m], b[m], b[k])).material_index = side
    if bottom:
        bm.faces.new(list(reversed(a))).material_index = cap_mat if cap_mat is not None else side
    if top:
        bm.faces.new(b).material_index = cap_mat if cap_mat is not None else side


def box(bm, centre, size, mat, frame=None):
    """An axis-aligned box (or along `frame`, a 3x3 matrix) with its base at `centre`."""
    sx, sy, sz = size
    corners = [Vector((x, y, z)) for z in (0, sz) for y in (-sy / 2, sy / 2) for x in (-sx / 2, sx / 2)]
    rot = frame or Matrix.Identity(3)
    v = [bm.verts.new(Vector(centre) + rot @ c) for c in corners]
    for q in ((0, 2, 3, 1), (4, 5, 7, 6), (0, 1, 5, 4), (2, 6, 7, 3), (0, 4, 6, 2), (1, 3, 7, 5)):
        bm.faces.new([v[i] for i in q]).material_index = mat


def tower():
    bm = bmesh.new()
    foot = RADIUS - 12.0
    stage(bm, PLINTH[0], foot, PLINTH[0] * 0.75, PLINTH[1], bottom=False)
    shaft_top = GALLERY[1] - GALLERY[2]      # up to the gallery's underside
    stage(bm, SHAFT_FOOT, PLINTH[1], SHAFT[0], shaft_top, bottom=False, top=False)

    def shaft_r(z):
        return SHAFT_FOOT + (SHAFT[0] - SHAFT_FOOT) * (z - PLINTH[1]) / (shaft_top - PLINTH[1])
    g_top = GALLERY[1]
    stage(bm, GALLERY[0], g_top - GALLERY[2], GALLERY[0], g_top, side=TRIM, cap_mat=TRIM)
    stage(bm, CAB_FOOT[0], CAB_FOOT[1] - 1, CAB_FOOT[0], CAB_FOOT[1], bottom=False, top=False)
    stage(bm, CAB_FOOT[0], CAB_FOOT[1], CAB_TOP[0], CAB_TOP[1], side=GLASS, bottom=False, top=False)
    stage(bm, CAB_TOP[0], CAB_TOP[1], ROOF[0], ROOF[1], side=TRIM, bottom=True, top=True)
    stage(bm, CAP[0], ROOF[1], CAP[0] * 0.8, CAP[1], bottom=False)
    # mullions between the windows, one on each corner of the cab
    for (x0, y0), (x1, y1) in zip(octagon(CAB_FOOT[0] + 0.3), octagon(CAB_TOP[0] + 0.3)):
        a, b = Vector((x0, y0, CAB_FOOT[1])), Vector((x1, y1, CAB_TOP[1]))
        up = (b - a).normalized()
        out = Vector((x0, y0, 0)).normalized()
        side = up.cross(out).normalized()
        box(bm, a, (0.9, 0.9, (b - a).length), TRIM, Matrix((side, up.cross(side), up)).transposed())
    # a red band round the shaft, as on the reference's stepped tower
    z0, z1 = PLINTH[1] + 9, PLINTH[1] + 12
    stage(bm, shaft_r(z0) + 0.6, z0, shaft_r(z1) + 0.6, z1, side=TRIM, cap_mat=TRIM)
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-3)
    return bm


def mast(bm, base, up, height, thick):
    """A thin mast along `up` from `base`, a cross bar on tall ones, a red light on top."""
    up = up.normalized()
    side = up.cross(Vector((0, 0, 1)) if abs(up.z) < 0.9 else Vector((1, 0, 0))).normalized()
    frame = Matrix((side, up.cross(side), up)).transposed()
    box(bm, base, (thick * 3, thick * 3, 2.0), HULL, frame)             # foot
    box(bm, base, (thick, thick, height), HULL, frame)
    if height > 20:
        for t in (0.55, 0.8):
            box(bm, base + up * height * t, (thick * 9, thick * 0.8, thick * 0.8), HULL, frame)
    box(bm, base + up * height, (TIP, TIP, TIP), LIGHT, frame)


def antennas():
    bm = bmesh.new()
    for x, y, h, t in ROOF_MASTS:
        mast(bm, Vector((x, y, CAP[1] if (x, y) == (0.0, 0.0) else ROOF[1])), Vector((0, 0, 1)), h, t)
    dish_n = dish_frame()[0].normalized()
    clear = math.cos((DISH_R + RIM[0] + 10) / RADIUS)
    for az, lat, h, t in HULL_MASTS:
        a, l = math.radians(az), math.radians(lat)
        n = Vector((math.cos(l) * math.cos(a), math.cos(l) * math.sin(a), math.sin(l)))
        if n.dot(dish_n) > clear:
            continue                    # no masts in or at the dish
        if any(n.dot(surface(haz, hlat)[3]) > math.cos(30.0 / RADIUS) for haz, hlat in HARDPOINTS):
            continue                    # nor on a turret's emplacement
        if any(n.dot(surface(maz, mlat)[3]) > math.cos(MOD_CLEAR / RADIUS) for _, maz, mlat in MODULES):
            continue                    # nor on a module
        mast(bm, n * (RADIUS - 1.0), n, h, t)
    return bm


def outline(grow):
    """The door's outline grown by `grow`, top corners chamfered (x, z), going round."""
    x, z0, z1 = W / 2 + grow, FLOOR - grow, FLOOR + H + grow
    c = CHAMFER + grow * (math.sqrt(2) - 1)
    return [(-x, z0), (x, z0), (x, z1 - c), (x - c, z1), (-x + c, z1), (-x, z1 - c)]


def hull():
    bm = bmesh.new()
    bmesh.ops.create_uvsphere(bm, u_segments=SEGMENTS, v_segments=RINGS, radius=RADIUS)
    # turn so a column of faces, not an edge, faces the door
    bmesh.ops.rotate(bm, verts=bm.verts, matrix=Matrix.Rotation(math.pi / SEGMENTS, 3, 'Z'))
    ring = math.pi / RINGS
    belt = [f for f in bm.faces
            if abs(math.asin(max(-1, min(1, f.calc_center_median().z / RADIUS)))) < ring * BELT_RINGS]
    out = bmesh.ops.extrude_face_region(bm, geom=belt)
    for v in (e for e in out["geom"] if isinstance(e, bmesh.types.BMVert)):
        r = Vector((v.co.x, v.co.y, 0))
        v.co += r.normalized() * BELT_OUT
    bmesh.ops.delete(bm, geom=belt, context='FACES')
    # the mouth: slice along the portal's outline, drop what is inside
    cut_hole(bm, Vector((0, -RADIUS, 0)), Vector((1, 0, 0)), Vector((0, 0, 1)), outline(FRAME))
    # the dish's hole, just inside the rim's outer edge
    o, u, v = dish_frame()
    rim = DISH_R + RIM[0] - 0.5
    cut_hole(bm, o, u, v, [(rim * math.cos(2 * math.pi * (k + 0.5) / DISH_SIDES),
                             rim * math.sin(2 * math.pi * (k + 0.5) / DISH_SIDES)) for k in range(DISH_SIDES)])
    relief(bm)
    return bm


def relief(bm):
    """Raised and sunk panels over the free hull: not on the belt, nor near the mouth, the dish,
    the tower or an emplacement."""
    rng = random.Random(RELIEF_SEED)
    clear = [(Vector((0, -1, 0)), math.hypot(W / 2 + FRAME, H / 2 + FRAME) + RELIEF_CLEAR),
             (dish_frame()[0].normalized(), DISH_R + RIM[0] + RELIEF_CLEAR),
             (Vector((0, 0, 1)), PLINTH[0] + RELIEF_CLEAR)]
    clear += [(surface(az, lat)[3], HP_FOOT[0] + RELIEF_CLEAR) for az, lat in HARDPOINTS]
    clear = [(d, r / RADIUS) for d, r in clear]
    belt = math.pi / RINGS * BELT_RINGS + 0.01

    def free(f):
        if len(f.verts) != 4:
            return False
        d = f.calc_center_median().normalized()
        if abs(math.asin(max(-1.0, min(1.0, d.z)))) < belt:
            return False
        return all(d.angle(a) > r for a, r in clear)
    # faces are tracked by index: bmesh's Python wrappers can't be kept in sets reliably
    bm.faces.index_update()
    # bmesh's own ops leave faces in memory order: sort by place so every run is the same
    where = lambda f: tuple(round(c, 2) for c in f.calc_center_median())
    faces = sorted((f for f in bm.faces if free(f)), key=where)
    pool = {f.index for f in faces}
    rng.shuffle(faces)
    used, panels, fittings = set(), [], []
    for f in faces:
        if f.index in used or rng.random() > RELIEF_SHARE:
            continue
        panel, cur = [f], f
        used.add(f.index)
        for _ in range(rng.choice(RELIEF_GROW)):
            near = sorted({h.index: h for e in cur.edges for h in e.link_faces
                           if h.index in pool and h.index not in used}.values(), key=where)
            if not near:
                break
            cur = rng.choice(near)
            panel.append(cur)
            used.add(cur.index)
        panels.append(panel)
    for panel in panels:
        bmesh.ops.inset_region(bm, faces=panel, thickness=RELIEF_INSET, depth=0, use_even_offset=True)
        normal = sum((f.normal for f in panel), Vector()).normalized()
        raised = rng.random() < RELIEF_RAISED
        h = rng.uniform(*RELIEF_RAISE) if raised else -rng.uniform(*RELIEF_SINK)
        out = bmesh.ops.extrude_face_region(bm, geom=panel)
        bmesh.ops.translate(bm, vec=normal * h,
                            verts=[v for v in out["geom"] if isinstance(v, bmesh.types.BMVert)])
        if raised:
            paint = TRIM if rng.random() < RELIEF_RED else PLAIN
            for f in out["geom"]:
                if isinstance(f, bmesh.types.BMFace):
                    f.material_index = paint
        bmesh.ops.delete(bm, geom=panel, context='FACES')
        if not raised and rng.random() < RELIEF_DETAIL:
            floor = [f for f in out["geom"] if isinstance(f, bmesh.types.BMFace) and f.normal.dot(normal) > 0.7]
            # bmesh hands its output back in memory order: sort it so every run is the same
            floor.sort(key=where)
            fittings.append((floor, normal))
    for floor, normal in fittings:
        # one kind per panel, fitted facet by facet: a panel can be L-shaped or bend over the hull
        kind = rng.choice(("vent", "vent", "pipes", "pipes", "hatch"))
        if not floor:
            continue
        if kind == "hatch":
            floor = [max(floor, key=lambda f: f.calc_area())]
        for f in floor:
            fitting(bm, f, kind, rng)


def fitting(bm, face, kind, rng):
    """A vent, a run of pipes or a hatch on one facet of a sunk panel's floor, kept inside it."""
    verts = list(face.verts)
    centre = face.calc_center_median()
    normal = face.normal.copy()
    edge = max(face.edges, key=lambda e: e.calc_length())
    t1 = (edge.verts[1].co - edge.verts[0].co)
    t1 = (t1 - normal * t1.dot(normal)).normalized()
    t2 = normal.cross(t1)
    frame = Matrix((t1, t2, normal)).transposed()
    # the largest box round the centre that stays inside the facet (it may be a trapezoid)
    a = min(abs((v.co - centre).dot(t1)) for v in verts) * 0.8
    b = min(abs((v.co - centre).dot(t2)) for v in verts) * 0.8
    at = lambda x, y, z=0.0: centre + t1 * x + t2 * y + normal * z
    if kind == "vent":
        # slats across the short side, a dark gap between each
        long_x = a >= b
        n = max(3, int((a if long_x else b) * 2 / 2.4))
        span = a if long_x else b
        for i in range(n):
            o = -span + (i + 0.5) * 2 * span / n
            size = (1.0, b * 2, 0.9) if long_x else (a * 2, 1.0, 0.9)
            box(bm, at(o, 0) if long_x else at(0, o), size, HULL, frame)
    elif kind == "pipes":
        along_x = a >= b
        count = rng.randint(2, 3)
        span = b if along_x else a
        for i in range(count):
            o = -span * 0.6 + i * (1.2 * span / max(1, count - 1))
            th = rng.uniform(1.0, 1.8)
            length = (a if along_x else b) * 2
            box(bm, at(0, o) if along_x else at(o, 0), (length, th, th) if along_x else (th, length, th), HULL, frame)
            if rng.random() < 0.5:                                  # a red coupling
                c = rng.uniform(-0.6, 0.6) * (a if along_x else b)
                box(bm, at(c, o, -0.2) if along_x else at(o, c, -0.2),
                    (1.6, th + 0.6, th + 0.6) if along_x else (th + 0.6, 1.6, th + 0.6), TRIM, frame)
    else:
        side = min(a, b) * 1.4
        box(bm, at(0, 0), (side + 1.2, side + 1.2, 0.5), TRIM, frame)       # frame
        box(bm, at(0, 0, 0.5), (side, side, 0.4), HULL, frame)               # lid
        box(bm, at(side * 0.15, 0, 0.9), (side * 0.5, 0.8, 0.5), HULL, frame)  # handle
        box(bm, at(side / 2 + 0.2, side / 2 + 0.2, 0.5), (0.9, 0.9, 0.6), LIGHT, frame)


def cut_hole(bm, origin, u, v, pts):
    """Slice the hull along an outline (in u, v round `origin`) seen along u x v; drop what is inside."""
    axis = u.cross(v)
    out = origin.normalized()
    near = lambda f: (f.calc_center_median() - origin).dot(out) > -RADIUS * 0.6

    def uv(p):
        d = p - origin
        return d.dot(u), d.dot(v)
    for (xa, za), (xb, zb) in zip(pts, pts[1:] + pts[:1]):
        a = origin + u * xa + v * za
        b = origin + u * xb + v * zb
        normal = (b - a).cross(axis).normalized()
        front = [f for f in bm.faces if near(f)]
        geom = list(dict.fromkeys(x for f in front for x in f.verts)) + list(dict.fromkeys(e for f in front for e in f.edges)) + front
        bmesh.ops.bisect_plane(bm, geom=geom, plane_co=a, plane_no=normal)

    def inside(p):
        x, z = uv(p)
        for (xa, za), (xb, zb) in zip(pts, pts[1:] + pts[:1]):
            if (xb - xa) * (z - za) - (zb - za) * (x - xa) < -1e-4:
                return False
        return True
    dead = [f for f in bm.faces if near(f) and inside(f.calc_center_median())]
    bmesh.ops.delete(bm, geom=dead, context='FACES')
    bmesh.ops.delete(bm, geom=[x for x in bm.verts if not x.link_faces], context='VERTS')


def dish_frame():
    """The dish's centre on the hull and two axes across it (u round the sphere, v towards the pole)."""
    a, l = math.radians(DISH_AT[0]), math.radians(DISH_AT[1])
    n = Vector((math.cos(l) * math.cos(a), math.cos(l) * math.sin(a), math.sin(l)))
    u = Vector((0, 0, 1)).cross(n).normalized()
    return n * RADIUS, u, n.cross(u)


def dish():
    bm = bmesh.new()
    o, u, v = dish_frame()
    n = u.cross(v)
    at = lambda x, y, z: bm.verts.new(o + u * x + v * y + n * z)
    ring = lambda r, z: [at(r * math.cos(2 * math.pi * (k + 0.5) / DISH_SIDES),
                            r * math.sin(2 * math.pi * (k + 0.5) / DISH_SIDES), z) for k in range(DISH_SIDES)]
    rim_z = RIM[1]
    # the bowl: a faceted paraboloid from the middle to the rim
    rings = []
    for i in range(1, DISH_RINGS + 1):
        r = DISH_R * i / DISH_RINGS
        rings.append(ring(r, rim_z - DISH_DEPTH * (1 - (r / DISH_R) ** 2)))
    hub_r = DISH_R / DISH_RINGS
    hub = ring(hub_r, rim_z - DISH_DEPTH * (1 - (hub_r / DISH_R) ** 2))
    bm.faces.new(hub).material_index = HULL
    for a, b in zip(rings, rings[1:]):
        for k in range(DISH_SIDES):
            m = (k + 1) % DISH_SIDES
            bm.faces.new((a[k], b[k], b[m], a[m])).material_index = HULL
    # the rim: a flat ring, its outer wall going down into the hull
    lip_in = rings[-1]
    lip_out = ring(DISH_R + RIM[0], rim_z)
    skirt = ring(DISH_R + RIM[0], -RIM[2])
    for k in range(DISH_SIDES):
        m = (k + 1) % DISH_SIDES
        bm.faces.new((lip_in[k], lip_out[k], lip_out[m], lip_in[m])).material_index = TRIM
        bm.faces.new((lip_out[k], skirt[k], skirt[m], lip_out[m])).material_index = HULL
    # the feed: a katana-like section, a thicker base, its top cut at a slant
    floor = rim_z - DISH_DEPTH * (1 - (hub_r / DISH_R) ** 2)
    feed(bm, o, u, v, n, floor)
    return bm


def feed(bm, o, u, v, n, floor):
    """The katana-section antenna: a thicker base, a red body cut at a slant, the hollow sleeve."""
    at = lambda x, y, z: bm.verts.new(o + u * x + v * y + n * z)
    sec = [(x, y) if FEED_SPINE_UP else (-x, y) for x, y in FEED_SECTION]
    # section's long side runs towards the pole (v): x -> v, y -> u
    s0 = FEED_BASE[0]
    blade(bm, at, [(x * s0, y * s0) for x, y in sec], floor - 2, floor + FEED_BASE[1], 0, HULL)
    body_top = floor + FEED_BASE[1] + FEED_BODY
    blade(bm, at, sec, floor + FEED_BASE[1], body_top, FEED_CHAMFER, TRIM, top_mat=HULL)
    sleeve(bm, at, sec, body_top, o, u, v, n)


def lone_feed(bm, p, u, v, n, rng):
    """The dish's antenna standing alone on the hull, on an octagonal pad with a red rim."""
    frame = Matrix((u, v, n)).transposed()
    pad = lambda r, z: [bm.verts.new(p + u * r * math.cos(math.pi / 8 + k * math.pi / 4) +
                                     v * r * math.sin(math.pi / 8 + k * math.pi / 4) + n * z) for k in range(8)]
    lo, hi, rim = pad(15.0, -3.0), pad(13.0, 2.0), pad(13.0, 2.6)
    for a, b, mat in ((lo, hi, HULL), (hi, rim, TRIM)):
        for k in range(8):
            m = (k + 1) % 8
            bm.faces.new((a[k], a[m], b[m], b[k])).material_index = mat
    top = pad(11.0, 2.6)
    for k in range(8):
        m = (k + 1) % 8
        bm.faces.new((rim[k], rim[m], top[m], top[k])).material_index = TRIM
    bm.faces.new(top).material_index = HULL
    feed(bm, p, u, v, n, 2.6)


def blade(bm, at, sec, z0, z1, chamfer, mat, top_mat=None):
    """A prism of `sec` (x towards the pole, y across) from z0 to z1, its top lower at the edge."""
    xs = [x for x, _ in sec]
    lo, hi = min(xs), max(xs)
    a = [at(y, -x, z0) for x, y in sec]
    b = [at(y, -x, z1 - chamfer * (x - lo) / (hi - lo)) for x, y in sec]
    n = len(sec)
    for k in range(n):
        m = (k + 1) % n
        bm.faces.new((a[k], a[m], b[m], b[k])).material_index = mat
    bm.faces.new(list(reversed(a))).material_index = mat
    bm.faces.new(b).material_index = mat if top_mat is None else top_mat


def inset(poly, t):
    """A convex polygon (counter-clockwise) moved in by t along every side."""
    lines = []
    for (xa, ya), (xb, yb) in zip(poly, poly[1:] + poly[:1]):
        d = Vector((xb - xa, yb - ya)).normalized()
        nrm = Vector((-d.y, d.x))                       # inward for a ccw polygon
        lines.append((Vector((xa, ya)) + nrm * t, d))
    out = []
    for (p0, d0), (p1, d1) in zip(lines[-1:] + lines[:-1], lines):
        den = d0.x * d1.y - d0.y * d1.x
        s = ((p1.x - p0.x) * d1.y - (p1.y - p0.y) * d1.x) / den
        q = p0 + d0 * s
        out.append((q.x, q.y))
    return out


def sleeve(bm, at, sec, body_top, o, u, v, n):
    """A hollow sleeve on the feed's cut top, two thin rods standing in it."""
    xs = [x for x, _ in sec]
    lo, hi = min(xs), max(xs)
    share = lambda x: (x - lo) / (hi - lo)
    base = lambda x: body_top - FEED_CHAMFER * share(x)
    mouth = lambda x: body_top + SLEEVE_LEN - (FEED_CHAMFER + SLEEVE_CHAMFER) * share(x)
    outer = inset(sec, SLEEVE_INSET)
    inner = inset(outer, SLEEVE_WALL)
    ob = [at(y, -x, base(x) - 0.3) for x, y in outer]
    ot = [at(y, -x, mouth(x)) for x, y in outer]
    ib = [at(y, -x, base(x) + 0.05) for x, y in inner]
    it = [at(y, -x, mouth(x)) for x, y in inner]
    k_n = len(outer)
    for k in range(k_n):
        m = (k + 1) % k_n
        bm.faces.new((ob[k], ob[m], ot[m], ot[k])).material_index = HULL     # outside
        bm.faces.new((ib[m], ib[k], it[k], it[m])).material_index = HULL     # inside, facing in
        bm.faces.new((ot[k], ot[m], it[m], it[k])).material_index = HULL     # the mouth's lip
    bm.faces.new(ib).material_index = HULL                                    # floor
    frame = Matrix((u, v, n)).transposed()
    for x, y, length, thick, light in SLEEVE_RODS:
        foot = o + u * y - v * x + n * base(x)
        box(bm, foot, (thick, thick, length), HULL, frame)
        if light:
            box(bm, foot + n * length, (TIP, TIP, TIP), LIGHT, frame)


def surface(az, lat):
    """A point on the hull (on the belt when lat is 0) and the frame there: u east, v north, n out."""
    a, l = math.radians(az), math.radians(lat)
    if abs(lat) < 180.0 / RINGS * BELT_RINGS:
        n = Vector((math.cos(a), math.sin(a), 0))
        p = n * (RADIUS + BELT_OUT) * math.cos(math.pi / SEGMENTS) + Vector((0, 0, RADIUS * math.sin(l)))
    else:
        n = Vector((math.cos(l) * math.cos(a), math.cos(l) * math.sin(a), math.sin(l)))
        p = n * RADIUS
    u = Vector((0, 0, 1)).cross(n).normalized()
    return p, u, n.cross(u), n


def emplacement(bm, p, u, v, n, rng):
    """A turret's emplacement: a sunk foot, a deck with a red lip, makeshift armour round it,
    an ammunition box with its conduit into the hull, a red light on one plate."""
    frame = Matrix((u, v, n)).transposed()
    at = lambda x, y, z: p + u * x + v * y + n * z
    turn = rng.uniform(0, math.pi * 2)

    def ring(r, z, sides=8):
        return [bm.verts.new(at(r * math.cos(turn + 2 * math.pi * k / sides),
                                r * math.sin(turn + 2 * math.pi * k / sides), z)) for k in range(sides)]

    def band(a, b, mat, patchy=False):
        for k in range(len(a)):
            m = (k + 1) % len(a)
            f = bm.faces.new((a[k], a[m], b[m], b[k]))
            f.material_index = (TRIM if rng.random() < 0.35 else HULL) if patchy else mat
    r0, r1, sunk, rise = HP_FOOT
    deck_r, deck_t = HP_DECK
    foot_lo, foot_hi = ring(r0, -sunk), ring(r1, rise)
    band(foot_lo, foot_hi, HULL, patchy=True)                   # mismatched plating
    bm.faces.new(foot_hi).material_index = HULL
    # a red collar, then the deck with a lip round it
    c0 = ring(deck_r + HP_LIP, rise)
    c1 = ring(deck_r + HP_LIP, rise + HP_COLLAR)
    band(c0, c1, TRIM)
    top = rise + HP_COLLAR + deck_t
    lip = ring(deck_r + HP_LIP, top + 0.4)
    band(c1, lip, TRIM)
    deck = ring(deck_r, top)
    band(lip, deck, TRIM)                                       # the lip's top, sloping in
    bm.faces.new(deck).material_index = HULL
    # armour plates round part of the rim, leaning out, uneven, some patched
    plates = rng.randint(2, 4)
    start = rng.uniform(0, math.pi * 2)
    lit = rng.randrange(plates)
    for i in range(plates):
        ang = start + i * (2 * math.pi / plates) + rng.uniform(-0.3, 0.3)
        width = rng.uniform(8.0, 12.0)
        height = rng.uniform(5.0, 8.0)
        lean = math.radians(rng.uniform(12, 28))
        out = Vector((math.cos(ang), math.sin(ang), 0))
        side = Vector((-math.sin(ang), math.cos(ang), 0))
        up = (Vector((0, 0, 1)) * math.cos(lean) + out * math.sin(lean))
        local = Matrix((side, up.cross(side) * -1, up)).transposed()
        base = Vector((0, 0, rise - 0.5)) + out * (deck_r + HP_LIP + 1.0)
        box(bm, at(*base), (width, 1.2, height), HULL, frame @ local)
        if rng.random() < 0.5:                      # a welded patch on it
            shift = side * rng.uniform(-width / 4, width / 4) + up * height * rng.uniform(0.2, 0.5)
            box(bm, at(*(base + shift - up.cross(side) * 0.9)), (width * 0.4, 0.5, height * 0.35),
                TRIM, frame @ local)
        if i == lit:
            box(bm, at(*(base + up * height)), (TIP, TIP, TIP), LIGHT, frame @ local)
    # ammunition or power box against the foot, its conduit down into the hull
    ang = start + math.pi / plates + rng.uniform(-0.2, 0.2)
    out = Vector((math.cos(ang), math.sin(ang), 0))
    side = Vector((-math.sin(ang), math.cos(ang), 0))
    local = Matrix((side, Vector((0, 0, 1)).cross(side) * -1, Vector((0, 0, 1)))).transposed()
    bx = out * (r1 + 2.5)
    box(bm, at(bx.x, bx.y, -1.0), (rng.uniform(5, 7), 4.0, rng.uniform(3.5, 5)), TRIM, frame @ local)
    cx = out * (r1 + 5.2)
    box(bm, at(cx.x, cx.y, -4.0), (1.2, 1.2, 4.5), HULL, frame @ local)
    return at(0, 0, top), frame


def hardpoints():
    """Emplacements on the hull, and an empty on each deck for the turret (z along the hull's normal)."""
    import random
    rng = random.Random(HP_SEED)
    bm = bmesh.new()
    marks = []
    for az, lat in HARDPOINTS:
        p, u, v, n = surface(az, lat)
        seat, frame = emplacement(bm, p, u, v, n, rng)
        marks.append((seat, frame))
    col = bpy.data.collections.get("HardpointMarks") or bpy.data.collections.new("HardpointMarks")
    if col.name not in bpy.context.scene.collection.children:
        bpy.context.scene.collection.children.link(col)
    for ob in list(col.objects):
        bpy.data.objects.remove(ob)
    for i, (seat, frame) in enumerate(marks, 1):
        e = bpy.data.objects.new(f"Hardpoint_{i:02d}", None)
        e.empty_display_type = 'ARROWS'
        e.empty_display_size = 8
        e.matrix_world = Matrix.Translation(seat) @ frame.to_4x4()
        col.objects.link(e)
    return bm


def cylinder(bm, a, b, r, mat, sides=8, caps=True):
    """A faceted cylinder from a to b."""
    axis = (b - a).normalized()
    s = axis.cross(Vector((0, 0, 1)) if abs(axis.z) < 0.9 else Vector((1, 0, 0))).normalized()
    t = axis.cross(s)
    ring = lambda c: [bm.verts.new(c + (s * math.cos(2 * math.pi * k / sides) +
                                        t * math.sin(2 * math.pi * k / sides)) * r) for k in range(sides)]
    ra, rb = ring(a), ring(b)
    for k in range(sides):
        m = (k + 1) % sides
        bm.faces.new((ra[k], ra[m], rb[m], rb[k])).material_index = mat
    if caps:
        bm.faces.new(list(reversed(ra))).material_index = mat
        bm.faces.new(rb).material_index = mat


def tanks(bm, p, u, v, n, rng):
    """Three tanks lying along the hull on struts, banded red, a pipe into the hull."""
    lift, r, length = 13.0, 5.0, 34.0
    for i, off in enumerate((-11.0, 0.0, 11.0)):
        c = p + n * lift + v * off
        cylinder(bm, c - u * length / 2, c + u * length / 2, r, HULL)
        for f in (-0.3, 0.3):
            cylinder(bm, c + u * (length * f - 1), c + u * (length * f + 1), r + 0.4, TRIM)
        for f in (-0.32, 0.32):
            box(bm, p + v * off + u * length * f - n * 3, (2.0, 2.0, lift - r + 3.5), HULL,
                Matrix((u, v, n)).transposed())
    cylinder(bm, p + n * lift + v * 11 + u * length / 2, p + n * lift + v * 11 + u * (length / 2 + 6), 1.4, TRIM)
    elbow = p + n * lift + v * 11 + u * (length / 2 + 6)
    cylinder(bm, elbow, elbow - n * (lift + 3), 1.4, TRIM)


def containers(bm, p, u, v, n, rng):
    """A platform on legs with containers stacked on it, mismatched and a little askew."""
    frame = Matrix((u, v, n)).transposed()
    lift = 7.0
    box(bm, p + n * lift, (38.0, 26.0, 1.5), HULL, frame)
    for x in (-16.0, 16.0):
        for y in (-10.0, 10.0):
            box(bm, p + u * x + v * y - n * 3, (2.0, 2.0, lift + 3), HULL, frame)
    size = (16.0, 7.0, 7.0)
    for x in (-8.6, 8.6):
        for y in (-7.6, 0.0, 7.6):
            layers = rng.choice((1, 1, 2))
            for z in range(layers):
                yaw = math.radians(rng.uniform(-4, 4))
                local = Matrix.Rotation(yaw, 3, 'Z')
                base = p + n * (lift + 1.5 + z * size[2]) + u * x + v * y
                box(bm, base, size, TRIM if rng.random() < 0.35 else HULL, frame @ local)
                box(bm, base + n * (size[2] * 0.5) + u * size[0] * 0.5, (0.4, size[1] * 0.7, size[2] * 0.7),
                    HULL, frame @ local)                                   # the doors' end


def habitat(bm, p, u, v, n, rng):
    """A crew pod on a tube: a row of lit windows down each side, an antenna on its roof."""
    frame = Matrix((u, v, n)).transposed()
    lift = 14.0
    cylinder(bm, p - n * 3, p + n * lift, 4.0, HULL)
    cylinder(bm, p + n * (lift - 4), p + n * (lift - 2), 4.6, TRIM)
    w, d, h = 32.0, 14.0, 11.0
    base = p + n * lift
    box(bm, base, (w, d, h), HULL, frame)
    box(bm, base + n * h, (w - 6, d - 5, 3.0), HULL, frame)               # roof
    box(bm, base + n * (h * 0.15), (w + 1.0, d + 0.6, 1.2), TRIM, frame)   # a red band low down
    for side in (-1, 1):
        for k in range(7):
            x = -w / 2 + 3.5 + k * (w - 7) / 6
            box(bm, base + u * x + v * side * (d / 2 + 0.05) + n * (h * 0.45), (2.4, 0.3, 2.2), GLASS, frame)
    mast(bm, base + n * (h + 3) + u * 8, n, 18.0, 0.7)


def keel():
    """Under the south pole, the tower's counterweight: stages, radiator fins, a light at its tip."""
    bm = bmesh.new()
    z = -RADIUS + 10
    stage(bm, 18.0, z - 18, 26.0, z, top=False)
    stage(bm, 13.0, z - 21, 18.0, z - 18, side=TRIM, cap_mat=TRIM, top=False)
    stage(bm, 7.0, z - 46, 13.0, z - 21, top=False)
    stage(bm, 2.5, z - 52, 7.0, z - 46, top=False)
    for k in range(4):
        ang = math.pi / 4 + k * math.pi / 2
        d = Vector((math.cos(ang), math.sin(ang), 0))
        tangent = Vector((-math.sin(ang), math.cos(ang), 0))
        frame = Matrix((tangent, d, Vector((0, 0, -1)))).transposed()
        box(bm, d * 22 + Vector((0, 0, z - 14)), (1.4, 22.0, 26.0), HULL, frame)
        box(bm, d * 32.6 + Vector((0, 0, z - 14)), (2.0, 1.2, 26.0), TRIM, frame)   # red edge
    box(bm, Vector((0, 0, z - 52 - TIP)), (TIP, TIP, TIP), LIGHT)
    return bm


def modules():
    import random as _r
    rng = _r.Random(MOD_SEED)
    bm = keel()
    build = {"tanks": tanks, "containers": containers, "habitat": habitat, "feed": lone_feed}
    for kind, az, lat in MODULES:
        p, u, v, n = surface(az, lat)
        build[kind](bm, p, u, v, n, rng)
    return bm


def prism(bm, pts, y0, y1, caps=True):
    """A prism along y from a closed outline in x, z."""
    a = [bm.verts.new((x, y0, z)) for x, z in pts]
    b = [bm.verts.new((x, y1, z)) for x, z in pts]
    n = len(pts)
    for i in range(n):
        j = (i + 1) % n
        bm.faces.new((a[i], a[j], b[j], b[i]))
    if caps:
        bm.faces.new(list(reversed(a)))
        bm.faces.new(b)


def ring_face(bm, outer, inner, y):
    """The flat ring between two outlines of the same corner count, at y."""
    o = [bm.verts.new((x, y, z)) for x, z in outer]
    i = [bm.verts.new((x, y, z)) for x, z in inner]
    n = len(outer)
    for k in range(n):
        m = (k + 1) % n
        bm.faces.new((o[k], o[m], i[m], i[k]))


def portal():
    bm = bmesh.new()
    inner, outer = outline(0), outline(FRAME)
    # the frame: outer walls from inside the hull to the front, the front ring
    o0 = [bm.verts.new((x, PORTAL_BACK, z)) for x, z in outer]
    o1 = [bm.verts.new((x, PORTAL_FRONT, z)) for x, z in outer]
    for k in range(len(outer)):
        m = (k + 1) % len(outer)
        bm.faces.new((o0[k], o0[m], o1[m], o1[k]))
    ring_face(bm, outer, inner, PORTAL_FRONT)
    # the hood: a slanted block over the door, standing out of the frame
    top = FLOOR + H + FRAME
    hx = W / 2 - CHAMFER
    prism(bm, [(-hx, top - FRAME), (hx, top - FRAME), (hx, top + 4), (-hx, top + 4)],
          PORTAL_FRONT - HOOD_OUT, PORTAL_FRONT + 0.01)
    # the deck: a slab under the door, its front corners cut
    lx = W / 2 + FRAME
    y0, y1 = PORTAL_FRONT - LIP_OUT, PORTAL_FRONT + 0.01
    zt, zb = FLOOR, FLOOR - LIP_THICK
    deck = bmesh.new()
    pts = [(-lx + 10, y0), (lx - 10, y0), (lx, y0 + 10), (lx, y1), (-lx, y1), (-lx, y0 + 10)]
    t = [bm.verts.new((x, y, zt)) for x, y in pts]
    b = [bm.verts.new((x, y, zb)) for x, y in pts]
    bm.faces.new(t)
    bm.faces.new(list(reversed(b)))
    for k in range(len(pts)):
        m = (k + 1) % len(pts)
        bm.faces.new((b[k], b[m], t[m], t[k]))
    deck.free()
    for f in bm.faces:
        f.material_index = TRIM
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    portal_detail(bm)
    return bm


def frame_ring(bm, outer, inner, y0, y1, mat):
    """A solid ring between two outlines (x, z) of the same corner count, from y0 to y1."""
    n = len(outer)
    o0 = [bm.verts.new((x, y0, z)) for x, z in outer]
    o1 = [bm.verts.new((x, y1, z)) for x, z in outer]
    i0 = [bm.verts.new((x, y0, z)) for x, z in inner]
    i1 = [bm.verts.new((x, y1, z)) for x, z in inner]
    for k in range(n):
        m = (k + 1) % n
        for f in ((o0[k], o0[m], i0[m], i0[k]), (o1[k], i1[k], i1[m], o1[m]),
                  (o0[k], o1[k], o1[m], o0[m]), (i0[k], i0[m], i1[m], i1[k])):
            bm.faces.new(f).material_index = mat


def belt_point(az, z):
    """The point on the belt's outer face at azimuth az (radians) and height z, and its normal."""
    step = 2 * math.pi / SEGMENTS
    mid = round(az / step) * step        # the hull is turned half a step: facets' middles sit at k step
    lat_edge = math.pi / RINGS * BELT_RINGS
    zr = RADIUS * math.sin(lat_edge)
    r_mid, r_edge = RADIUS, RADIUS * math.cos(lat_edge)
    r = r_mid + (r_edge - r_mid) * min(1.0, abs(z) / zr) + BELT_OUT     # corner radius at that height
    apothem = r * math.cos(step / 2)
    normal = Vector((math.cos(mid), math.sin(mid), 0))
    d = Vector((math.cos(az), math.sin(az), 0))
    p = d * (apothem / d.dot(normal))
    return Vector((p.x, p.y, z)), normal


def portal_detail(bm):
    """Bezel, tunnel ribs and hydraulic rams."""
    first_new = len(bm.faces)
    # a black bezel standing out round the frame's front, a red lip round the opening
    frame_ring(bm, outline(FRAME), outline(FRAME - BEZEL[0]), PORTAL_FRONT - BEZEL[1], PORTAL_FRONT, HULL)
    frame_ring(bm, outline(1.2), outline(0), PORTAL_FRONT - 1.0, PORTAL_FRONT, HULL)
    # ribs lining the tunnel, black and red by turns
    y, k = PORTAL_FRONT + 4.0, 0
    while y < DOOR_Y - DOOR_THICK - 3.0:
        frame_ring(bm, outline(0), outline(-1.4), y, y + 1.6, TRIM if k % 3 == 2 else HULL)
        y += TUNNEL_RIBS
        k += 1
    # hydraulic rams from the frame's sides to mounts bolted on the belt
    for sx in (-1, 1):
        for z in PISTONS:
            a = Vector((sx * (W / 2 + FRAME), PORTAL_FRONT + 8.0, z))
            az = math.atan2(-RADIUS, sx * (W / 2 + FRAME + 22.0))
            b, normal = belt_point(az, z)
            mount = b + normal * 5.0                    # a post holds the ram's end clear of the hull
            mid = a.lerp(mount, 0.55)
            cylinder(bm, a, mid, 1.3, TRIM)
            cylinder(bm, mid, mount, 2.6, HULL)
            side = Vector((0, 0, 1)).cross(normal).normalized()
            plate = Matrix((side, normal.cross(side), normal)).transposed()
            box(bm, b - normal * 1.5, (8.0, 8.0, 2.5), HULL, plate)             # its plate on the belt
            box(bm, b, (3.4, 3.4, 5.0 + 2.6), TRIM, plate)                      # and the post
            box(bm, a - Vector((0, 0, 2.5)), (2.0, 4.0, 5.0), HULL)        # its mount on the frame
    # teeth under the hood
    top = FLOOR + H + FRAME
    hx = W / 2 - CHAMFER
    for i in range(7):
        x = -hx + 4 + i * (2 * hx - 8) / 6
        box(bm, Vector((x, PORTAL_FRONT - HOOD_OUT + 1.5, top - FRAME - 2.0)), (2.4, 3.0, 2.0), HULL)
    bm.faces.ensure_lookup_table()
    bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces)[first_new:])


GUIDE_STEP = 4.0           # spacing of the centre line's lights
GUIDE_SIZE = (1.4, 3.0, 0.4)  # each light: across, along, high


def guide_lights():
    """One line of red lights down the middle of the deck, outside the portal only. UV2 "Chase"
    holds each light's place along the line (u, 0 at the deck's edge, 1 at the portal) so a
    shader can run a pulse inwards, as on a runway."""
    bm = bmesh.new()
    y0, y1 = PORTAL_FRONT - LIP_OUT + 2.5, PORTAL_FRONT - 2.5
    count = int((y1 - y0) // GUIDE_STEP) + 1
    places = []
    for k in range(count):
        y = y0 + k * GUIDE_STEP
        first = len(bm.faces)
        box(bm, Vector((0, y, FLOOR)), GUIDE_SIZE, LIGHT)
        bm.faces.ensure_lookup_table()
        places += [k / max(1, count - 1)] * (len(bm.faces) - first)
    return bm, places


def door_leaf(side):
    """One leaf (side -1 left, +1 right), its pivot at the doors' middle; it opens along x by
    side x DOOR_SLIDE."""
    bm = bmesh.new()
    o = DOOR_OVERLAP
    x, z0, z1 = W / 2 + o, FLOOR - o, FLOOR + H + o
    c = CHAMFER
    half = [(0, z0), (x, z0), (x, z1 - c), (x - c, z1), (0, z1)]
    pts = [(side * px, pz) for px, pz in half]
    if side < 0:
        pts.reverse()
    y0, y1 = -DOOR_THICK / 2, DOOR_THICK / 2
    prism(bm, pts, y0, y1)
    for f in bm.faces:
        f.material_index = HULL
    face = y0 - 0.01                              # the outer face looks down the tunnel (-y)
    # the meeting edge: a red band with notches, a black frame round the leaf
    box(bm, Vector((side * 1.0, face, z0 + o)), (2.0, 0.8, H), TRIM)
    for i in range(6):
        if (i + (side > 0)) % 2:
            box(bm, Vector((side * 2.6, face, z0 + o + 2 + i * (H - 4) / 5)), (1.2, 1.0, 3.0), HULL)
    box(bm, Vector((side * (x - 2.5), face, z0 + o)), (2.0, 0.8, H - c), HULL)
    # ribs across, a hatch for crew, warning lights at the top of the seam
    for zr in (FLOOR + 8, FLOOR + 18, FLOOR + 28):
        box(bm, Vector((side * (x / 2 + 1), face - 0.2, zr)), (x - 8, 1.0, 1.8), HULL)
    box(bm, Vector((side * (x - 10), face - 0.3, FLOOR)), (6.0, 0.6, 7.0), TRIM)
    box(bm, Vector((side * 2.0, face - 0.5, FLOOR + H - 3)), (TIP, TIP, TIP), LIGHT)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return bm


def room():
    """The hangar's inside, normals facing in; its walls run out to the portal's front."""
    bm = bmesh.new()
    inner = outline(0)
    y1 = ROOM_FRONT + D
    v_front = [bm.verts.new((x, PORTAL_FRONT, z)) for x, z in inner]
    v_back = [bm.verts.new((x, y1, z)) for x, z in inner]
    n = len(inner)
    for k in range(n):
        m = (k + 1) % n
        bm.faces.new((v_front[m], v_front[k], v_back[k], v_back[m]))
    bm.faces.new(v_back)
    # every face towards the room's middle line (x 0, z 0): seen from inside, culled from outside
    bm.normal_update()
    flip = [f for f in bm.faces
            if f.normal.dot(Vector((0, f.calc_center_median().y, 0)) - f.calc_center_median()) < 0
            and abs(f.normal.y) < 0.5 or abs(f.normal.y) >= 0.5 and f.normal.y > 0]
    bmesh.ops.reverse_faces(bm, faces=flip)
    return bm


def atlas_material():
    m = bpy.data.materials.get("SyntyAtlas") or bpy.data.materials.new("SyntyAtlas")
    m.use_nodes = True
    nodes, links = m.node_tree.nodes, m.node_tree.links
    nodes.clear()
    out = nodes.new("ShaderNodeOutputMaterial")
    bsdf = nodes.new("ShaderNodeBsdfPrincipled")
    tex = nodes.new("ShaderNodeTexImage")
    tex.image = bpy.data.images.load(ATLAS, check_existing=True)
    glow = nodes.new("ShaderNodeTexImage")
    glow.image = bpy.data.images.load(EMISSIVE, check_existing=True)
    # the belt's region of the atlas (light grey panels) is darkened to the hull's grey
    uvn = nodes.new("ShaderNodeUVMap")
    sep_uv = nodes.new("ShaderNodeSeparateXYZ")
    links.new(uvn.outputs["UV"], sep_uv.inputs["Vector"])
    bu0, bu1, bv0, bv1 = BELT_PANEL
    mask = None
    for axis, lo, hi in ((0, bu0, bu1), (1, 1.0 - bv1, 1.0 - bv0)):
        for op, edge in (('GREATER_THAN', lo), ('LESS_THAN', hi)):
            m_ = nodes.new("ShaderNodeMath"); m_.operation = op
            links.new(sep_uv.outputs[axis], m_.inputs[0]); m_.inputs[1].default_value = edge
            if mask is None:
                mask = m_
            else:
                both = nodes.new("ShaderNodeMath"); both.operation = 'MULTIPLY'
                links.new(mask.outputs[0], both.inputs[0]); links.new(m_.outputs[0], both.inputs[1])
                mask = both
    dark = nodes.new("ShaderNodeMix"); dark.data_type = 'RGBA'; dark.blend_type = 'MULTIPLY'
    links.new(mask.outputs[0], dark.inputs["Factor"])
    links.new(tex.outputs["Color"], dark.inputs[6])
    dark.inputs[7].default_value = (BELT_TINT, BELT_TINT, BELT_TINT, 1)
    links.new(dark.outputs[2], bsdf.inputs["Base Color"])
    # cyan lights (blue over red) become GLOW_TINT at their brightness; red and orange stay
    sep = nodes.new("ShaderNodeSeparateColor")
    links.new(glow.outputs["Color"], sep.inputs["Color"])
    cyan = nodes.new("ShaderNodeMath"); cyan.operation = 'GREATER_THAN'
    links.new(sep.outputs[2], cyan.inputs[0]); links.new(sep.outputs[0], cyan.inputs[1])
    bright = nodes.new("ShaderNodeRGBToBW")
    links.new(glow.outputs["Color"], bright.inputs["Color"])
    tinted = nodes.new("ShaderNodeMix"); tinted.data_type = 'RGBA'; tinted.blend_type = 'MULTIPLY'
    tinted.inputs["Factor"].default_value = 1.0
    links.new(bright.outputs["Val"], tinted.inputs[6])
    tinted.inputs[7].default_value = (*GLOW_TINT, 1)
    pick = nodes.new("ShaderNodeMix"); pick.data_type = 'RGBA'
    links.new(cyan.outputs["Value"], pick.inputs["Factor"])
    links.new(glow.outputs["Color"], pick.inputs[6])
    links.new(tinted.outputs[2], pick.inputs[7])
    links.new(pick.outputs[2], bsdf.inputs["Emission Color"])
    bsdf.inputs["Emission Strength"].default_value = 1.5
    bsdf.inputs["Roughness"].default_value = 0.8
    links.new(bsdf.outputs["BSDF"], out.inputs["Surface"])
    return m


BELT_TOP = RADIUS * math.sin(math.pi / RINGS * BELT_RINGS)


def on_belt(poly):
    """A face of the belt's outer band (not its top and bottom steps)."""
    c = poly.center
    return (abs(c.z) < BELT_TOP and Vector((c.x, c.y, 0)).length > RADIUS + BELT_OUT * 0.5
            and abs(poly.normal.z) < 0.3)


def belt_plate(centre, rng, plates):
    """The belt's column a point is in: its horizontal axis, origin, where it starts in the atlas
    and metres to atlas units (the belt's height fills the region's height)."""
    step = 2 * math.pi / SEGMENTS
    lon = (math.atan2(centre.y, centre.x) - math.pi / SEGMENTS) % (2 * math.pi)
    i = int(lon // step)
    key = ("belt", i)
    if key not in plates:
        a0 = math.pi / SEGMENTS + i * step
        a1 = a0 + step
        r = RADIUS + BELT_OUT + 1
        p0 = Vector((math.cos(a0), math.sin(a0), 0)) * r
        p1 = Vector((math.cos(a1), math.sin(a1), 0)) * r
        t1 = (p1 - p0).normalized()
        u0, u1, v0, v1 = BELT_PANEL
        k = (v1 - v0) / (2 * BELT_TOP)
        width = (p1 - p0).length * k
        plates[key] = (t1, p0, rng.uniform(u0, max(u0, u1 - width)), v0, k)
    return plates[key]


def on_shell(poly):
    """A face lying on the hull's skin (or the belt's), not a panel's side wall or a fitting."""
    d = poly.center.normalized()
    return poly.normal.dot(d) > 0.85


def plate(centre, rng, plates):
    """The sphere cell a point is in, and its projection: axes, origin, where its piece of the
    panel starts in the atlas and how much it is shrunk to fit."""
    step_u, step_v = 2 * math.pi / SEGMENTS, math.pi / RINGS
    d = centre.normalized()
    lon = (math.atan2(d.y, d.x) - math.pi / SEGMENTS) % (2 * math.pi)
    lat = math.asin(max(-1.0, min(1.0, d.z)))
    i, j = int(lon // step_u), int((lat + math.pi / 2) // step_v)
    if (i, j) in plates:
        return (i, j), plates[(i, j)]
    lon_c = math.pi / SEGMENTS + (i + 0.5) * step_u
    lat_c = -math.pi / 2 + (j + 0.5) * step_v
    n = Vector((math.cos(lat_c) * math.cos(lon_c), math.cos(lat_c) * math.sin(lon_c), math.sin(lat_c)))
    t1 = Vector((0, 0, 1)).cross(n)
    t1 = t1.normalized() if t1.length > 1e-3 else Vector((1, 0, 0))
    t2 = n.cross(t1)
    corners = []
    for a in (i, i + 1):
        for b in (j, j + 1):
            lo, la = math.pi / SEGMENTS + a * step_u, -math.pi / 2 + b * step_v
            corners.append(Vector((math.cos(la) * math.cos(lo), math.cos(la) * math.sin(lo), math.sin(la)))
                           * (RADIUS + BELT_OUT + 3))
    xs, ys = [c.dot(t1) for c in corners], [c.dot(t2) for c in corners]
    origin = t1 * min(xs) + t2 * min(ys)
    u0, u1, v0, v1 = PANEL
    scale = (u1 - u0) / PANEL_METRES
    w, h = (max(xs) - min(xs)) * scale, (max(ys) - min(ys)) * scale
    fit = min(1.0, (u1 - u0) / w, (v1 - v0) / h)
    ou = rng.uniform(u0, u1 - w * fit)
    ov = rng.uniform(v0, v1 - h * fit)
    plates[(i, j)] = (t1, t2, origin, ou, ov, fit)
    return (i, j), plates[(i, j)]


def synty_uvs(ob, rng):
    """UVs into the Synty atlas, Synty's way: most faces take one flat swatch, the hull's big
    faces show the dark tech panel, projected flat at a fixed scale."""
    me = ob.data
    uv = me.uv_layers.get("UVMap") or me.uv_layers.new(name="UVMap")
    by_mat = {HULL: "hull", TRIM: "trim", GLASS: "glass", LIGHT: "light", PLAIN: "black"}
    u0, u1, v0, v1 = PANEL
    scale = (u1 - u0) / PANEL_METRES
    plates = {}
    for poly in me.polygons:
        loops = list(poly.loop_indices)
        if ob.name == "HangarInterior":
            sw = SWATCH["room"]
        elif poly.material_index == HULL and ob.name == "Hull" and on_belt(poly):
            # the belt: one vertical projection per column of facets, the big grey panels
            t1, origin, ou, ov, k = belt_plate(poly.center, rng, plates)
            for i in loops:
                co = me.vertices[me.loops[i].vertex_index].co
                uu = ou + (co - origin).dot(t1) * k
                vv = ov + (BELT_TOP - co.z) * k
                uv.data[i].uv = (uu, 1.0 - vv)
            continue
        elif poly.material_index == HULL and ob.name == "Hull" and on_shell(poly):
            # one projection per cell of the original sphere: every face in it (the halves of a
            # split quad, a panel's floor or top) shows the same piece of the panel, so the only
            # seams are on the facets' real edges
            cell, (t1, t2, origin, ou, ov, fit) = plate(poly.center, rng, plates)
            for i in loops:
                co = me.vertices[me.loops[i].vertex_index].co - origin
                uu = ou + co.dot(t1) * scale * fit
                vv = ov + co.dot(t2) * scale * fit
                uv.data[i].uv = (uu, 1.0 - vv)
            continue
        else:
            name = by_mat.get(poly.material_index, "hull")
            if name == "hull" and rng.random() < 0.25:
                name = "hull_light"
            elif name == "trim" and rng.random() < 0.3:
                name = "trim_dark"
            elif name == "black" and rng.random() < 0.4:
                name = "black_soft"
            sw = SWATCH[name]
        for i in loops:
            uv.data[i].uv = (sw[0], 1.0 - sw[1])


def build():
    hull_mat = material("PirateHull", (0.06, 0.06, 0.07, 1))
    trim_mat = material("PirateTrim", (0.45, 0.04, 0.04, 1))
    room_mat = material("HangarInterior", (0.25, 0.26, 0.28, 1))
    glass_mat = material("PirateGlass", (0.9, 0.25, 0.1, 1), glow=2.0)
    light_mat = material("PirateLight", (1.0, 0.05, 0.03, 1), glow=6.0)
    parts = [hull_mat, trim_mat, glass_mat, light_mat]
    made = [new_object("Hull", hull(), parts),
            new_object("Portal", portal(), parts),
            new_object("HangarInterior", room(), room_mat),
            new_object("Tower", tower(), parts),
            new_object("Antennas", antennas(), parts),
            new_object("Dish", dish(), parts),
            new_object("Hardpoints", hardpoints(), parts),
            new_object("Modules", modules(), parts),
            new_object("DoorLeft", door_leaf(-1), parts),
            new_object("DoorRight", door_leaf(1), parts)]
    guide_bm, guide_places = guide_lights()
    made.append(new_object("GuideLights", guide_bm, parts))
    for name, side in (("DoorLeft", -1), ("DoorRight", 1)):
        ob = bpy.data.objects[name]
        ob.location = (0, DOOR_Y, 0)
        ob["slide_axis"] = "x"
        ob["slide_distance"] = side * DOOR_SLIDE
    atlas = atlas_material()
    rng = random.Random(3)
    for ob in made:
        synty_uvs(ob, rng)
        ob.data.materials.clear()
        ob.data.materials.append(atlas)
    chase = bpy.data.objects["GuideLights"].data.uv_layers.new(name="Chase")
    for poly, place in zip(bpy.data.objects["GuideLights"].data.polygons, guide_places):
        for i in poly.loop_indices:
            chase.data[i].uv = (place, 0.0)
    for ob in made:
        print(ob.name, len(ob.data.polygons), "faces",
              sum(len(p.vertices) - 2 for p in ob.data.polygons), "tris")

    return made


# Blender to Godot: x stays, Blender's z is Godot's y, Blender's -y is Godot's z
def godot(v):
    return Vector((v.x, v.z, -v.y))


def turrets():
    """Where each turret sits, as tscn transforms: base up along the hull's normal, laser and missile in turn."""
    scales = (7.09, 9.67)
    for i in range(1, len(HARDPOINTS) + 1):
        e = bpy.data.objects[f"Hardpoint_{i:02d}"]
        m = e.matrix_world
        up = godot(m.to_3x3() @ Vector((0, 0, 1))).normalized()
        ahead = godot(m.to_3x3() @ Vector((0, 1, 0))).normalized()
        side = up.cross(ahead)
        k = scales[(i - 1) % 2]
        x, y, z = side * k, up * k, ahead * k
        p = godot(m.translation)
        print(f"TURRET {i:02d} {'missile' if i % 2 == 0 else 'laser'} Transform3D("
              + ", ".join(f"{c:.5g}" for c in (x.x, y.x, z.x, x.y, y.y, z.y, x.z, y.z, z.z, p.x, p.y, p.z)) + ")")


def export(objs, path):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    bpy.ops.object.select_all(action='DESELECT')
    for o in objs:
        o.hide_set(False)
        o.select_set(True)
    bpy.ops.export_scene.gltf(filepath=path, use_selection=True, export_format='GLB',
                              export_yup=True, export_apply=True, export_texcoords=True,
                              export_normals=True, export_materials='PLACEHOLDER',
                              export_animations=False, export_extras=False)
    print("wrote", path)


SEEN_ONLY = ("DoorLeft", "DoorRight", "GuideLights")

made = build()
export(made, os.path.join(OUT, "redhold.glb"))
export([o for o in made if o.name not in SEEN_ONLY], os.path.join(OUT, "redhold_collision.glb"))
turrets()
