# Mesh parts: taking pieces out of a model

Some models come with their moving bits modelled into one big mesh: the
Synty cockpit has its sliders, switches and buttons baked into the cockpit
body. To make each of them a scene of its own (a control that can be wired,
duplicated and placed anywhere), the **mesh part tool** cuts them out:

- every part is saved as a mesh of its own, in its own frame (so its scene
  can move it along its own axes: a knob along Y, a lever about X);
- the model's mesh is saved without them (the *remainder*), to put in the
  model's place so nothing is there twice;
- a report gives each part's transform in the scene, to place its scene
  exactly where it was modelled.

Code: `scripts/Tools/` (`MeshPartTool`, `MeshPartList`, `MeshPart`). Tool
scene: `tools/mesh_parts/mesh_part_tool.tscn`. Lists, one per model:
`tools/mesh_parts/lists/`.

## The licence

The meshes it writes are made from the Synty pack, whose licence does not
allow redistributing it: they go to `assets/synty/parts/` (ignored by git,
like the rest of `assets/synty/`). The lists and the scenes that use the
meshes are ours and go in the repository. On a new checkout, run the
import with Godot to make the meshes again:

```sh
dotnet build
cmake -DGODOT=<path to the Godot .NET binary> -P tools/import_synty.cmake
```

That copies the Synty files, has Godot import them and runs the tool on
every list in `tools/mesh_parts/lists/`.

## A list

A `MeshPartList` resource (create one in the FileSystem dock: New Resource,
MeshPartList; or copy `synty_pov_cockpit_01.tres`):

| Property | What it is |
|---|---|
| `Source` | The model: an imported FBX or glTF, or a scene |
| `SourceMesh` | The mesh to cut: a node path inside the model (`SM_Veh_POV_Cockpit_01`) |
| `Placement` | Where the model sits in the scene the parts are for. The cockpit turns its Synty model half a turn (Synty's models face +Z): `Transform3D(-1, 0, 0, 0, 1, 0, 0, 0, -1, 0, 0, 0)`. **All boxes, origins and the report are in that scene's coordinates**, so you can read them off the editor |
| `OutputFolder` | Where the meshes and the report go (`res://assets/synty/parts/<model>`) |
| `RemainderName` | The file name of the model's mesh without the parts (empty: do not save it) |
| `Parts` | The parts, in order (see below) |
| `SurveyBox` | The box the survey looks in |

Each `MeshPart`:

| Property | What it is |
|---|---|
| `Name` | The mesh's file name (`slider_knob` becomes `slider_knob.res`) |
| `Mode` | `Extract`: saved and taken out of the model. `Copy`: saved, the model keeps it. `Remove`: only taken out (something else will cover the spot) |
| `Box` | The part is every triangle that lies whole inside this box |
| `Origin`, `RotationDegrees` | The part's frame: the saved mesh is expressed in it. Choose it so the part moves along its own axes: a slider's frame lies along its track, a switch's on its plate. Parts that work together (a knob and its track) should share a frame, so their meshes line up when both sit at the scene's origin |

**Order matters.** A triangle belongs to the first part whose box holds
it. A knob sits inside its track's box: list the knob before the track.

## Finding the boxes: the survey

The survey lists the loose pieces (connected triangles) inside a box, with
each one's box (already grown by half a millimetre) and centre, ready to
paste:

```sh
godot --headless --path . res://tools/mesh_parts/mesh_part_tool.tscn -- res://tools/mesh_parts/lists/synty_pov_cockpit_01.tres survey -0.32 0.34 -0.95 0.12 0.18 0.25
```

The six numbers are the box (x, y, z, size x, size y, size z) in the
scene's coordinates; without them it uses the list's `SurveyBox`. A good
way to find a region: put a `MeshInstance3D` with a `BoxMesh` over the
piece in the scene, read its position and size, and survey around it.

```
Loose pieces inside AABB(-0.32, 0.34, -0.95, 0.12, 0.18, 0.25):
  surface 0,   10 triangles  box AABB(-0.2244, 0.3720, -0.8478, 0.0235, 0.1019, 0.0367)  centre Vector3(-0.2126, 0.4230, -0.8294)
  surface 0,   22 triangles  box AABB(-0.2198, 0.4129, -0.8325, 0.0144, 0.0326, 0.0175)  centre Vector3(-0.2126, 0.4292, -0.8238)
```

Here a slider's track (10 triangles, long in y) and its knob (22, inside
the track's box).

## Extracting

From the editor: open `tools/mesh_parts/mesh_part_tool.tscn`, set `List`
and `Job` on the node, run the scene (F6). From the command line:

```sh
godot --headless --path . res://tools/mesh_parts/mesh_part_tool.tscn -- res://tools/mesh_parts/lists/synty_pov_cockpit_01.tres extract
```

It prints the report and saves it next to the meshes
(`<list name>_report.txt`):

```
slider_knob: 22 triangles -> res://assets/synty/parts/pov_cockpit_01/slider_knob.res
  transform  Transform3D(1, 0, 0, 0, 0.95105654, 0.309017, 0, -0.309017, 0.95105654, -0.212609, 0.422951, -0.829434)
  bounds in its frame  AABB(-0.0067, -0.0112, 0.0034, 0.0134, 0.0316, 0.0082)
```

- **transform**: put the part's scene there (in the scene the list is for)
  and it sits where it was modelled;
- **bounds in its frame**: how big it is and where it sits, along its own
  axes: what you need to work out a slider's travel or a lever's hinge.

A part that finds no triangles is reported (and the tool exits with 1):
its box is wrong, or an earlier part took its triangles.

Then give the remainder to the model's mesh node in its scene through a
`ModelMeshes` node under the model's instance (the cockpit: `Body/Meshes`,
`SM_Veh_POV_Cockpit_01` -> `cockpit_body.res`; a mesh set on the imported
node itself is lost in an export), and use the part meshes in control
scenes (see [controls.md](../controls.md)).

## Example: the cockpit's slider

`tools/mesh_parts/lists/synty_pov_cockpit_01.tres` takes out the slider
by the RWR (knob and track, sharing the frame of the track's middle, tilted
18 degrees like the panel), the switch to the right of the radar (lever and
plate, sharing the plate's frame), and removes the sliders and the second
switch that the panels cover or replace. From the report: the track runs
from -0.0527 to +0.0535 along its Y and the knob is 0.0316 tall, modelled
with its middle at +0.0046; so the knob's travel is from -0.0415 to
+0.0331 metres off where it was modelled: the `Bottom` and `Top` of
`scenes/controls/sliders/synty_cockpit_slider.tscn`.
