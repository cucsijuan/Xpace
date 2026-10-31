# Making big meshes fit for levels of detail. The engine picks a mesh's level
# by how far the camera is from the whole of it, so one mesh for a room you
# stand in, or for a station's ring, is always drawn at its finest. Here the
# loose small parts of such a mesh (bolts, brackets) go to meshes of their own,
# which the game drops from a few tens of metres (their names end in SMALL: see
# DetailRange), and the rest is cut into columns, each of which then picks its
# own level.

import bmesh, bpy

SMALL = "_small"


def keep_faces(obj, faces):
    """Removes every face of the mesh but those (a set of indices)."""
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    bm.faces.ensure_lookup_table()
    bmesh.ops.delete(bm, geom=[f for f in bm.faces if f.index not in faces], context='FACES')
    bm.to_mesh(obj.data)
    bm.free()
    obj.data.update()


def islands(mesh, weld=0.002):
    """Groups of faces joined by touching corners (the mesh splits its vertices
    at hard edges, so by place, not by index): lists of polygon indices."""
    parent = {}

    def find(a):
        while parent.setdefault(a, a) != a:
            parent[a] = parent[parent[a]]
            a = parent[a]
        return a
    places = [tuple(round(c / weld) for c in v.co) for v in mesh.vertices]
    for p in mesh.polygons:
        first = find(places[p.vertices[0]])
        for i in p.vertices[1:]:
            parent[find(places[i])] = first
    groups = {}
    for p in mesh.polygons:
        groups.setdefault(find(places[p.vertices[0]]), []).append(p.index)
    return list(groups.values())


def small_parts(mesh, size):
    """The faces of the loose parts no wider than size in any direction."""
    small = set()
    for group in islands(mesh):
        vs = [mesh.vertices[i].co for f in group for i in mesh.polygons[f].vertices]
        if max(max(v[a] for v in vs) - min(v[a] for v in vs) for a in range(3)) < size:
            small.update(group)
    return small


def piece(obj, name, faces):
    """A copy of the mesh with only those faces, under that name."""
    new = obj.copy()
    new.data = obj.data.copy()
    new.name = name
    new.data.name = name
    bpy.context.collection.objects.link(new)
    keep_faces(new, faces)
    return new


def cut(obj, cell, small):
    """The mesh as pieces: its small parts by column (<name>_x_y_small), the
    rest by column of cell metres (<name>_x_y), by each face's middle. Removes
    the original; gives the pieces."""
    mesh = obj.data
    tiny = small_parts(mesh, small) if small else set()
    cells = {}
    if small:
        for group in islands(mesh):
            if group[0] in tiny:
                c = mesh.polygons[group[0]].center
                cells.setdefault((int(c.x // cell), int(c.y // cell), True), []).extend(group)
    for p in mesh.polygons:
        if p.index not in tiny:
            cells.setdefault((int(p.center.x // cell), int(p.center.y // cell), False), []).append(p.index)
    pieces = []
    for (x, y, is_small), faces in sorted(cells.items()):
        name = "%s_%d_%d%s" % (obj.name, x, y, SMALL if is_small else "")
        pieces.append(piece(obj, name, set(faces)))
    bpy.data.objects.remove(obj)
    return pieces


def split_small(obj, small):
    """A moving part stays whole, under its name; only its small parts go, to
    <name>_small."""
    tiny = small_parts(obj.data, small)
    if not tiny:
        return [obj]
    extra = piece(obj, obj.name + SMALL, tiny)
    keep_faces(obj, {p.index for p in obj.data.polygons} - tiny)
    return [obj, extra]
