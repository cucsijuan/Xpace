# Gives the landing platform's deck surfaces of their own, out of Synty's
# SM_Bld_Landing_Platform_01:
#
#   blender -b -P tools/station_bay/build_pad.py
#   python3 tools/station_bay/pad_maps.py
#
# The shape stays as Synty made it. The deck's faces go to meshes of their own,
# for ModelPaint's ByMesh, mapped flat from above; the relief is in normal maps:
#   Pad_Body:   everything else, on Synty's atlas as before
#   Pad_Paint:  the deck's panels, one unit of UV to the metre of the model
#   Pad_Plate:  the central plate, the same, with a second UV over the whole
#               plate for its dimples (Synty only drew them as dark circles)
#   Pad_Joints: the thin dark faces between them, one UV over the whole deck
#               for the map that sinks them
# The joints' outlines go to pad_joints.json, for pad_maps.py.

import bmesh, bpy, json, os, sys

sys.path.insert(0, os.path.dirname(__file__))
from atlas import SYNTY, CUT, export
from pad_layout import DECK_HALF, PLATE_HALF

SOURCE = os.path.join(SYNTY, "SM_Bld_Landing_Platform_01.fbx")
OUTPUT = os.path.join(CUT, "SM_Bld_Landing_Platform_Deep.glb")
JOINTS = os.path.join(os.path.dirname(__file__), "pad_joints.json")

JOINT_WIDTH = 0.15      # a face this narrow, of the dark swatches, on the deck, is a joint

# Atlas swatches, by a face's average UV to two places
DARK = {(0.23, 0.28), (0.24, 0.28)}
PANEL = {(0.24, 0.30), (0.25, 0.30)}
PLATE = {(0.72, 0.75), (0.76, 0.74), (0.75, 0.81)}


def swatch(face, uv):
    n = len(face.loops)
    return (round(sum(l[uv].uv.x for l in face.loops) / n, 2), round(sum(l[uv].uv.y for l in face.loops) / n, 2))


def width(face):
    """How wide a thin face is, near enough: its area over half its perimeter."""
    return 2 * face.calc_area() / max(1e-6, face.calc_perimeter())


def on_deck(face):
    c = face.calc_center_median()
    return abs(c.x) < DECK_HALF and abs(c.y) < DECK_HALF


def split(obj, name, indices):
    """A copy of the object with only those faces."""
    new = obj.copy()
    new.data = obj.data.copy()
    new.name = name
    new.data.name = name
    bpy.context.collection.objects.link(new)
    bm = bmesh.new()
    bm.from_mesh(new.data)
    bm.faces.ensure_lookup_table()
    bmesh.ops.delete(bm, geom=[f for f in bm.faces if f.index not in indices], context='FACES')
    bm.to_mesh(new.data)
    bm.free()
    new.data.update()
    return new


def main():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=SOURCE)
    obj = next(o for o in bpy.data.objects if o.type == 'MESH')
    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)

    bm = bmesh.new()
    bm.from_mesh(obj.data)
    uv = bm.loops.layers.uv.active
    bm.normal_update()
    up = [f for f in bm.faces if f.normal.z > 0.9]
    joints = {f for f in up if swatch(f, uv) in DARK and on_deck(f) and width(f) < JOINT_WIDTH
              and abs(f.calc_center_median().z) < 0.03}
    plate = {f for f in up if swatch(f, uv) in PLATE}
    panels = {f for f in up if on_deck(f) and swatch(f, uv) in PANEL}
    with open(JOINTS, "w") as out:
        json.dump([[[round(v.co.x, 4), round(v.co.y, 4)] for v in f.verts] for f in joints], out)
    for f in plate | panels:
        for l in f.loops:
            l[uv].uv = (l.vert.co.x, l.vert.co.y)
    for f in joints:
        for l in f.loops:
            l[uv].uv = (l.vert.co.x / (2 * DECK_HALF) + 0.5, l.vert.co.y / (2 * DECK_HALF) + 0.5)
    for f in bm.faces:
        f.smooth = False
    bm.faces.index_update()
    joint_i = {f.index for f in joints}
    plate_i = {f.index for f in plate}
    panel_i = {f.index for f in panels}
    body_i = {f.index for f in bm.faces} - joint_i - plate_i - panel_i
    bm.to_mesh(obj.data)
    bm.free()
    obj.data.update()

    parts = [split(obj, "Pad_Body", body_i), split(obj, "Pad_Paint", panel_i),
             split(obj, "Pad_Plate", plate_i), split(obj, "Pad_Joints", joint_i)]
    bpy.data.objects.remove(obj)
    mesh = parts[2].data
    second = mesh.uv_layers.new(name="UV2")
    for l in mesh.loops:
        co = mesh.vertices[l.vertex_index].co
        second.data[l.index].uv = (co.x / (2 * PLATE_HALF) + 0.5, co.y / (2 * PLATE_HALF) + 0.5)
    for p in parts:
        print(p.name, len(p.data.polygons), "faces", sum(len(f.vertices) - 2 for f in p.data.polygons), "triangles")
    export(parts, OUTPUT)


main()
