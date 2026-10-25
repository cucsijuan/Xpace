# Rock fields: asteroid fields and planetary rings

A `RockField` node (`scripts/Space/RockField.cs`) fills a region of space
with rocks. Nothing is placed by hand: space is cut into cells, and each
cell's rocks are worked out from the field's seed and the cell's place
whenever something needs them, the same on every machine. A field can be
as big as a planet's ring and costs only what is near the camera and the
ships.

## Setting one up

Add a `Node3D` with the `RockField` script where the field's centre should
be (its rotation turns the field: a ring lies in the node's XZ plane). Then:

| Property | What it is |
|---|---|
| `Region` | Where the rocks are: a `RockBox` (size, falloff at the faces) or a `RockRing` (inner and outer radius, thickness, falloffs, bands) |
| `Kinds` | The rocks it is made of: `RockKind` resources (`assets/space/rocks/`) |
| `Seed` | Another seed, other rocks |
| `CellSize`, `RocksPerCell` | How thick the field is: `RocksPerCell` rocks in each cell where the region's density is 1 |
| `MinScale`, `MaxScale`, `SizeSkew` | Rock sizes: the kind's size times a scale between the two; the higher the skew, the more small rocks |
| `DrawRange`, `SmallDrawRange`, `SmallSize` | How far rocks show: rocks under `SmallSize` across only to `SmallDrawRange` |
| `ChunkCells`, `ChunksPerFrame` | Cells per drawing chunk (per side), and how many chunks may be built in one frame |

The field's frame is taken from the node when the scene starts and kept in
double precision; floating origin shifts do not move it.

## Rock kinds

A `RockKind` names a model (an imported FBX, glTF or scene: its first mesh
is drawn), a material to draw it with, a weight (how often it turns up), a
scale, and its solid shape: a few spheres inside the model. Ships collide
with the spheres, rounds and missiles stop on them, and radars, radar
warners and heat seekers cannot see through them.

The spheres are fitted by the **rock shape tool** and saved in the kind's
resource, so every machine collides with the same shape, with or without
the art:

```sh
dotnet build
godot --headless --path . res://tools/rocks/rock_shape_tool.tscn                         # every kind in assets/space/rocks
godot --headless --path . res://tools/rocks/rock_shape_tool.tscn -- res://assets/space/rocks/rock_01.tres
```

It grids the model, finds the grid points inside it and each one's distance
to the surface, then picks spheres one at a time, each the one that covers
most of what is still uncovered, up to the kind's `MaxSpheres`. It reports
how much of the inside the spheres cover and how far the surface stands out
of them (what a ship can sink into the rock before it touches). More
spheres fit tighter; each one costs a little in every contact test.

The models are Synty's `SM_Env_Astroid_*` (copied by
`tools/import_synty.cmake`, not in the repository). Without them a kind
draws a plain ball the size of its spheres; the collisions do not change.

## Mining

Every rock has a name, `RockId` (its cell and its index in the cell).
`RockField.Remove(id)` takes a rock away for good: its cell and its chunk
are made again without it.
