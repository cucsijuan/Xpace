"""
Builds the T1 missile pylon (four rails, two rows of two) as a low-poly mesh painted from cells of Synty's
PolygonSciFiSpace atlas, so it matches the ships it hangs from. Run with Blender:

    blender -b --python tools/pylon/build_pylon.py -- assets/weapons/pylons/pylon_t1.glb

Godot frame (the hardpoint's): origin on top, where it meets the wing; +Y up, nose along -Z. The missiles hang
centred on the rails at (+-RAIL_X, RAIL_Y[0 or 1], 0): scenes/weapons/pylons/pylon_t1.tscn has a Marker3D on each.
"""
import sys

import bmesh
import bpy

RAIL_X = 0.2
RAIL_Y = (-0.3, -0.7)
MISSILE_RADIUS = 0.1

# Cells of the atlas (u, v as the image reads, v from the top)
GREY = (0.66, 0.0078)
ORANGE = (0.309, 0.616)


def to_blender(x, y, z):
    """Godot (+Y up, -Z ahead) to Blender (+Z up, glTF export turns it back)."""
    return (x, -z, y)


def box(bm, uv, cell, x0, x1, y0, y1, z0, z1, front_taper=1.0):
    """A box in Godot coordinates; front_taper shrinks its -Z end across and down (a streamlined nose)."""
    cx, top = (x0 + x1) / 2, y1
    corners = []
    for z in (z0, z1):
        t = front_taper if z == z0 else 1.0
        for y in (y0, y1):
            for x in (x0, x1):
                xx = cx + (x - cx) * t
                yy = top - (top - y) * t
                corners.append(bm.verts.new(to_blender(xx, yy, z)))
    v = lambda i, j, k: corners[k * 4 + j * 2 + i]
    faces = [
        (v(0, 0, 0), v(1, 0, 0), v(1, 1, 0), v(0, 1, 0)),
        (v(0, 0, 1), v(0, 1, 1), v(1, 1, 1), v(1, 0, 1)),
        (v(0, 0, 0), v(0, 0, 1), v(1, 0, 1), v(1, 0, 0)),
        (v(0, 1, 0), v(1, 1, 0), v(1, 1, 1), v(0, 1, 1)),
        (v(0, 0, 0), v(0, 1, 0), v(0, 1, 1), v(0, 0, 1)),
        (v(1, 0, 0), v(1, 0, 1), v(1, 1, 1), v(1, 1, 0)),
    ]
    made = []
    for f in faces:
        face = bm.faces.new(f)
        for loop in face.loops:
            loop[uv].uv = (cell[0], 1 - cell[1])
        made.append(face)
    return made


def build(path):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    mesh = bpy.data.meshes.new("PylonT1")
    bm = bmesh.new()
    uv = bm.loops.layers.uv.new("UVMap")

    # The mount against the wing, its nose swept back
    box(bm, uv, GREY, -0.06, 0.06, -0.14, 0.0, -0.55, 0.4, front_taper=0.45)
    # An orange band along each side of the mount
    box(bm, uv, ORANGE, -0.065, -0.06, -0.1, -0.05, -0.35, 0.3)
    box(bm, uv, ORANGE, 0.06, 0.065, -0.1, -0.05, -0.35, 0.3)
    # The spine between the two columns of missiles
    box(bm, uv, GREY, -0.025, 0.025, -0.66, -0.14, -0.4, 0.3, front_taper=0.7)
    for row, rail_y in enumerate(RAIL_Y):
        top = rail_y + MISSILE_RADIUS
        for side in (-1, 1):
            x = side * RAIL_X
            # The arm out from the spine, and the rail the missile hangs from
            arm = (0.025, x - side * 0.03) if side > 0 else (x - side * 0.03, -0.025)
            box(bm, uv, GREY, min(arm), max(arm), top + 0.02, top + 0.05, -0.2, 0.15)
            box(bm, uv, GREY, x - 0.03, x + 0.03, top, top + 0.05, -0.42, 0.28, front_taper=0.6)

    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)      # outward: the back of a face isn't drawn
    bm.to_mesh(mesh)
    bm.free()
    for p in mesh.polygons:
        p.use_smooth = False
    obj = bpy.data.objects.new("PylonT1", mesh)
    bpy.context.scene.collection.objects.link(obj)
    mat = bpy.data.materials.new("PolygonSciFiSpace")
    mesh.materials.append(mat)
    bpy.ops.export_scene.gltf(filepath=path, export_format="GLB", export_materials="PLACEHOLDER")
    print("FACES", len(mesh.polygons))


if __name__ == "__main__":
    build(sys.argv[sys.argv.index("--") + 1])
