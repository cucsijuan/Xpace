# Shared by the station bay scripts (build_hangar.py, cut_station.py): the
# station atlas' areas and texels, a builder of faces textured with them,
# and Blender helpers (import, boolean cut, glTF export).

import bpy, bmesh, math, os
from mathutils import Vector

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
SYNTY = os.path.join(ROOT, "assets", "synty")
CUT = os.path.join(SYNTY, "cut")
BAY = os.path.join(ROOT, "assets", "stations", "bay")

# Atlas areas (pixels of the 2048 px PolygonSciFiSpace_Texture_01_B), and
# single texels for flat colours. The glow_* texels are lit in the emissive map.
AREAS = {
    "greeble": (1040, 660, 1540, 1300),
    "panel": (360, 170, 1010, 620),
    "diamond": (1680, 10, 2040, 730),
    "hazard": (900, 1270, 1010, 1860),
}
TEXELS = {
    "dark": (1140, 708), "steel": (1236, 660), "grey": (1044, 12), "mid": (324, 36),
    "light": (36, 12), "pale": (1860, 1140),
    "glow_cyan": (770, 1936), "glow_white": (544, 1980), "glow_orange": (608, 1980),
    "glow_green": (800, 1980), "glow_red": (865, 1980), "glow_blue": (672, 1980),
}
MASK_WHITE = (10, 10)  # a white texel of the wear mask (second UV channel)


def uv(p):
    return (p[0] / 2048.0, 1.0 - p[1] / 2048.0)


def import_fbx(name):
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=os.path.join(SYNTY, name + ".fbx"))
    new = [o for o in bpy.data.objects if o not in before]
    for o in new:
        o.select_set(o.type == 'MESH')
    mesh = [o for o in new if o.type == 'MESH'][0]
    bpy.context.view_layer.objects.active = mesh
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    for o in new:
        if o is not mesh:
            bpy.data.objects.remove(o)
    mesh.name = name
    return mesh


def prism(name, bottom, top):
    bm = bmesh.new()
    lo = [bm.verts.new(v) for v in bottom]
    hi = [bm.verts.new(v) for v in top]
    n = len(lo)
    bm.faces.new(list(reversed(lo)))
    bm.faces.new(hi)
    for k in range(n):
        bm.faces.new((lo[k], lo[(k + 1) % n], hi[(k + 1) % n], hi[k]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    mesh = bpy.data.meshes.new(name)
    bm.to_mesh(mesh)
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.scene.collection.objects.link(obj)
    return obj


def cut(obj, tools, operation='DIFFERENCE'):
    bpy.context.view_layer.objects.active = obj
    for t in tools:
        mod = obj.modifiers.new("cut", 'BOOLEAN')
        mod.operation = operation
        mod.solver = 'EXACT'
        mod.use_hole_tolerant = True  # Synty's meshes are not all closed
        mod.use_self = True
        mod.object = t
        bpy.ops.object.modifier_apply(modifier=mod.name)


def export(objs, path):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    bpy.ops.object.select_all(action='DESELECT')
    for o in objs:
        o.select_set(True)
    bpy.ops.export_scene.gltf(filepath=path, use_selection=True, export_format='GLB',
                              export_yup=True, export_apply=True, export_texcoords=True,
                              export_normals=True, export_materials='PLACEHOLDER',
                              export_animations=False, export_extras=False)


class Builder:
    """Faces of the bay, each with its atlas area or flat texel."""

    def __init__(self):
        self.bm = bmesh.new()
        self.uv = self.bm.loops.layers.uv.new("UVMap")
        self.mask = self.bm.loops.layers.uv.new("Mask")

    def face(self, points, facing, look, corners=None):
        verts = [self.bm.verts.new(p) for p in points]
        normal = Vector()
        for i, a in enumerate(points):
            b = points[(i + 1) % len(points)]
            normal += Vector(((a.y - b.y) * (a.z + b.z), (a.z - b.z) * (a.x + b.x), (a.x - b.x) * (a.y + b.y)))
        if normal.dot(facing) < 0:
            verts.reverse()
            if corners:
                corners = list(reversed(corners))
        f = self.bm.faces.new(verts)
        for i, loop in enumerate(f.loops):
            if look in TEXELS:
                loop[self.uv].uv = uv(TEXELS[look])
            else:
                x0, y0, x1, y1 = AREAS[look]
                cu, cv = corners[i]
                loop[self.uv].uv = uv((x0 + (x1 - x0) * cu, y0 + (y1 - y0) * cv))
            loop[self.mask].uv = uv(MASK_WHITE)
        return f

    def quad(self, a, b, c, d, facing, look, tiles=(1, 1)):
        """a b c d round the quad, a to b along u; tiled into areas of the atlas."""
        nu, nv = tiles
        for i in range(nu):
            for j in range(nv):
                def at(s, t):
                    return a.lerp(b, s).lerp(d.lerp(c, s), t)
                s0, s1, t0, t1 = i / nu, (i + 1) / nu, j / nv, (j + 1) / nv
                self.face([at(s0, t0), at(s1, t0), at(s1, t1), at(s0, t1)], facing, look,
                          [(0, 1), (1, 1), (1, 0), (0, 0)])

    def band(self, lower, upper, facing_of, look, tile=12.0):
        """Quads between two loops of points (both closed, the same count)."""
        n = len(lower)
        for k in range(n):
            a, b = lower[k], lower[(k + 1) % n]
            c, d = upper[(k + 1) % n], upper[k]
            mid = (a + b + c + d) / 4
            tiles = (max(1, round((b - a).length / tile)), max(1, round((d - a).length / tile)))
            self.quad(a, b, c, d, facing_of(mid), look, tiles)

    def fill(self, outer, inner, facing, look):
        """The flat ring between two loops (inner may be empty), triangulated."""
        bm = bmesh.new()
        loops = [outer] + ([inner] if inner else [])
        edges = []
        for loop in loops:
            vs = [bm.verts.new(p) for p in loop]
            edges += [bm.edges.new((vs[i], vs[(i + 1) % len(vs)])) for i in range(len(vs))]
        result = bmesh.ops.triangle_fill(bm, use_beauty=True, use_dissolve=False, edges=edges)
        for f in result["geom"]:
            if isinstance(f, bmesh.types.BMFace):
                self.face([v.co.copy() for v in f.verts], facing, look,
                          [(0, 0), (1, 0), (0, 1)])
        bm.free()

    def wall(self, corner, along, width, levels, facing, holes=(), tile=14.0):
        """A flat upright wall from corner along a horizontal direction, in
        bands of levels [(top z, look), ..., (bottom z, None)], less the
        holes (u0, u1, z0, z1) cut through it."""
        def at(u, z):
            return Vector((corner.x + along.x * u, corner.y + along.y * u, z))

        def piece(u0, u1, z0, z1, look):
            if u1 - u0 > 0.01 and z1 - z0 > 0.01:
                tiles = (max(1, round((u1 - u0) / tile)), max(1, round((z1 - z0) / tile)))
                self.quad(at(u0, z0), at(u1, z0), at(u1, z1), at(u0, z1), facing, look, tiles)

        for (z1, look), (z0, _) in zip(levels, levels[1:]):
            spans = [(0.0, width)]
            for u0, u1, h0, h1 in holes:
                if h1 <= z0 or h0 >= z1:
                    continue
                # Above and below the hole within this band, then beside it
                piece(u0, u1, max(z0, h1), z1, look)
                piece(u0, u1, z0, min(z1, h0), look)
                spans = [part for s0, s1 in spans for part in ((s0, min(s1, u0)), (max(s0, u1), s1))]
            for s0, s1 in spans:
                piece(s0, s1, z0, z1, look)

    def block(self, centre, size, top, sides, turn=0.0):
        """A block standing on the cap (no bottom face), turned about Z."""
        hx, hy = size[0] / 2, size[1] / 2
        c, s = math.cos(turn), math.sin(turn)

        def at(x, y, z):
            return Vector((centre.x + x * c - y * s, centre.y + x * s + y * c, centre.z + z))
        base = [at(-hx, -hy, 0), at(hx, -hy, 0), at(hx, hy, 0), at(-hx, hy, 0)]
        high = [at(-hx, -hy, size[2]), at(hx, -hy, size[2]), at(hx, hy, size[2]), at(-hx, hy, size[2])]
        self.face(high, Vector((0, 0, 1)), top, [(0, 1), (1, 1), (1, 0), (0, 0)])
        for k in range(4):
            a, b = base[k], base[(k + 1) % 4]
            out = ((a + b) / 2 - centre)
            out.z = 0
            self.quad(a, b, high[(k + 1) % 4], high[k], out.normalized(), sides)

    def mesh(self, name):
        bmesh.ops.remove_doubles(self.bm, verts=self.bm.verts, dist=0.001)
        mesh = bpy.data.meshes.new(name)
        self.bm.to_mesh(mesh)
        mesh.materials.append(bpy.data.materials.new("SyntyStation"))
        obj = bpy.data.objects.new(name, mesh)
        bpy.context.scene.collection.objects.link(obj)
        return obj
