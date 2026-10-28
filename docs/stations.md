# Stations: modules, solids and avoidance capsules

A station is a scene of **modules** and **parts**, each a scene of its own. Put them together in the editor to build a station; nothing about a station is in code.

| Scene | What it is |
|---|---|
| `scenes/stations/synty_station.tscn` | The station from Synty's `Demo_Exterior` (the one in `scenes/main.tscn`) |
| `scenes/stations/modules/station_01_ring.tscn` | The big ring with spokes and the central drum (SM_Ship_Station_01, 566 m across) |
| `scenes/stations/modules/station_02_outer_ring.tscn` | A thin ring with arms, round the big one (SM_Ship_Station_02, 500 m) |
| `scenes/stations/modules/station_03_inner_ring.tscn` | A ring with spokes, inside the big one (SM_Ship_Station_03, 270 m) |
| `scenes/stations/modules/station_04_hub.tscn` | The hub under the ring (SM_Ship_Station_04, 258 m wide, 149 m tall) |
| `scenes/stations/modules/station_05_keel.tscn` | The keel and antenna below the hub (SM_Ship_Station_05, 292 m long) |
| `scenes/stations/modules/station_06_tower.tscn` | The control tower on top (SM_Ship_Station_06, 101 m tall) |
| `scenes/stations/props/turret_laser_double.tscn` | Laser turret (SM_Prop_Turret_Base_Double_06; the demo scales it 7.09) |
| `scenes/stations/props/turret_missile_double.tscn` | Missile turret (SM_Prop_Turret_Base_Double_08; scaled 9.67) |
| `scenes/stations/props/spacewalk_tube.tscn` | Spacewalk tube (SM_Prop_SpaceWalk_Tube_01; scaled 4.8) |
| `scenes/stations/modules/station_01_ring_hangar.tscn` | The big ring with the hangar's room cut out of its outer band |
| `scenes/stations/props/hangar_big.tscn` | The big hangar: a room for every ship short of a capital one, made out of Synty's SM_Bld_Hangar_01 |
| `scenes/stations/props/landing_pad.tscn` | Landing platform, looks and solid only (SM_Bld_Landing_Platform_01, 13.2 m) |
| `scenes/stations/props/landing_pad_small.tscn` | A working landing pad for small ships (fighters): the platform at twice its size, clamps, glow and beacon (see Landing pads) |
| `scenes/stations/props/hull_panel_a.tscn`, `_b`, `_c` | Hull panels, 30 x 30 m, cut from the ring's own skin and flattened |
| `scenes/stations/props/hull_trim.tscn` | A 1 m beam, to scale over panels' edges and joints |
| `scenes/stations/props/hangar_casing.tscn` | The hull round a big hangar's back, made of panels and trims |
| `scenes/stations/props/steam_vent.tscn` | Synty's FX_Steam: grey puffs rising from a vent |

The modules fit together at their origins, as in the demo: ring, outer ring, inner ring and tower all at (0, 0, 0), the hub at (0, -115.5, 0), the keel at (0, -228.9, 0). The turrets are only looks for now: they do not aim or fire.

The station modules use `assets/materials/synty_station.tres`, Synty's `PolygonScifiSpace_SpaceStation_01` material: the 01_B colour atlas multiplied by the station's wear mask (`PolygonSciFiSpace_Ship_Mask_Station_01`, on the second UV channel) and the emissive map for the windows. To paint a station another colour, duplicate the material and change its albedo texture to another of Synty's atlases (`Textures/Alts` in the pack: 01_A to 04_F). Add the atlas to the `TEXTURES` list in `tools/import_synty.cmake`, which copies it in, and to the private art repository.

## The big hangar

`scenes/stations/props/hangar_big.tscn` is a hangar 67 m wide, 98.5 m long and 23 m high inside, with a door 54 m wide and 22 m high: every ship short of a capital one parks on its floor (Synty's widest cruiser, Cruiser_03, is 37 x 50 m). It is Synty's SM_Bld_Hangar_01 (40 x 31 m, a door for fighters) built bigger by `tools/station_bay/build_hangar.py`, a Blender script (`tools/import_synty.cmake` runs it when given `-DBLENDER=...`):

- The walls are repeated, not stretched: along the room the side walls repeat a bay (a rib and its panel: they are laid out on a 3.75 m grid); across it the door wall repeats a 7 m length through the door. The back wall grows at two seams beside its corners instead, so its control room and small doors keep their size and stay in the middle. Up, the walls grow through a band where they are plain.
- Small loose parts (rails, pipes, ribs) crossing a seam move whole instead of stretching.
- Synty's floor (with a pit) and ceiling give way to ours, `assets/stations/bay/hangar_floor.glb` (diamond plate with a hazard border; panels and beams with strips of light; a roof and an underside outside): it is our own geometry on the station's atlas, so it is in the repository; the stretched Synty meshes go to `assets/synty/cut/`.
- The door's halves are raised by a `MeshSlide` node; the control room's glass has its own material through `ModelPaint.ByMesh`.
- The back wall is dressed (node `Dressing`, every piece an instance to move or delete in the editor): a run of pipes, vents, hanging and sagging cables, wire boxes, a pipe down to the control room's roof and machines and an antenna on it. The floor is left clear for pads.

In the demo station four hangars go through the big ring's outer band (226.5 to 279.5 m from the axis, -50 to -140 m high), midway between the ring's arms: 45 degrees either side of the spacewalk tubes and opposite them. Each one's door is in the band's outer face, its back stands clear in the ring's hollow (clad, see below); the missile turrets on the band stand well above them. `tools/station_bay/cut_station.py` cuts their rooms out of the ring (and the ring's collision mesh) and prints their transforms: the `HangarBig1` to `HangarBig4` nodes in `synty_station.tscn` must have the same ones (`ANGLES` in the script).

Where a hangar stands out of the ring, `hangar_casing.tscn` (instanced in `hangar_big.tscn`, so all four have it) clads it in the station's own hull: `Panels` and `Trims`, every one an instance to move, scale, add or delete in the editor (to make room for a corridor or a lift later). The raised door slides up into a pocket cut in the ring above the door, lined with plates, so the band's plates that stand out don't go through it.

## Landing pads

A hangar's door lets ships in; a **landing pad** inside takes them. `scenes/stations/props/landing_pad_small.tscn` is the one for small ships: its root node has the `LandingPad` script, everything else in the scene is looks (the platform, its solid, the glow). A pad for big ships is the same script with other numbers and other looks.

### How docking goes

1. The pilot asks the station for a dock (COMMS: REQUEST DOCKING). Traffic control clears a hangar and **one pad in it** that the ship fits on and that no other ship has. COMMS shows both (`HANGAR 1 PAD 2`). The door opens and the beacon lights.
2. Only that pad glows **red**, and only that pad takes the ship: over any other pad nothing happens. The radio hears the pad's beacon, and FLIGHT takes the RADAR screen to show the pad from above: the pad's square, the ship in the middle, and bars for yaw (bottom), roll (top), pitch (left) and height (right).
3. Over the pad, within its leeway, turned as it is and level, the glow turns **green**. All that is left is to come down.
4. Low enough (within `CaptureHeight` of its resting place) and slow, the clamps take the ship: the game moves it slowly (`SettleSeconds`) to its exact place. Docked, the thrusters no longer move it. The glow turns **blue**. The station shuts the door once every ship it cleared to that hangar is docked.
5. To leave, the pilot asks the station (COMMS: REQUEST LAUNCH). The door opens, the glow turns **amber**, the clamps lift the ship `LaunchHeight` and let it go. Once the ship is out of the hangar, the door shuts and the clearance is over.

A hangar can have several pads, and so hold several ships, one to a pad. The door is open while any ship cleared to the hangar is on its way in or out. A hangar with no pads works as before: it takes one ship at a time, the door opens, and the ship can come in and out, but nothing holds it.

### Putting a pad in a hangar

1. Open the hangar's scene (`scenes/stations/props/hangar_big.tscn`) and instance the pad scene into it, once per pad. It can go anywhere under the hangar's root, or under any node inside it. The pad finds the hangar's `HangarDock` (the `Dock` node) by itself. Pads are numbered in the order they are in the scene tree, from 1, unless you set `Number`.
2. Place it on the floor. The pad's origin is the **top of the platform**, its +Y is up, and its **-Z is where a docked ship's nose points**. The `Nose` marker in the scene shows that way. Turn the pad about Y to choose whether ships park facing the door or facing in.
3. Leave room. A ship's middle comes down over the pad's origin, so the whole ship must fit there: the pad's `MaxFootprint` plus some margin to the walls, and the hangar's door must let it through.
4. Run the game (or `tests/landing_pad_test.tscn`, which puts a pad in a hangar in code) and dock.

The station must be one whose hangars have a `HangarDock` node, as `hangar_big.tscn` has. Every station that uses the hangar scene gets the pad: the four hangars of the Synty station all share `hangar_big.tscn`.

### The pad's fields

| Field | Meaning |
|---|---|
| `Number` | Its number in the hangar, as the radio and COMMS give it (0: by its order in the scene) |
| `MaxFootprint` | The biggest ship it takes, width (x) by length (z), metres. Ships are measured by their hitboxes |
| `MaxHeight` | And its height |
| `Clearance` | The gap between the platform's top and a docked ship's lowest point, metres |
| `Leeway` | How far across from the pad's middle the ship's middle may be to count as over it, metres |
| `HeadingLeeway`, `TiltLeeway` | How far the nose may be turned from the pad's heading, and the ship tilted from level, degrees |
| `CaptureHeight` | How far above its resting place a lined-up ship must come down for the clamps |
| `CaptureSpeed`, `CaptureSpin` | The most it may be moving (m/s) and turning (degrees/s) for the clamps |
| `SettleSeconds` | How long the clamps take to bring it down to its place |
| `LaunchHeight`, `LaunchSeconds` | How high the clamps lift it before letting go, and how long that takes |
| `GuidanceRange` | How far the beacon's guidance reaches, metres |
| `GlowNode` | The name of the pad's child that holds the glow (`Glow`). Every light below it takes the state's colour, and every mesh gets it as the instance shader parameter `glow_color` (`assets/materials/pad_glow.gdshader` has it). All are hidden while the pad waits for nobody. It is a name, not a node path, on purpose: Godot rewrites node paths when you duplicate a node, and a duplicated pad then lit the first pad's glow |
| `WaitingColor`, `AlignedColor`, `HoldingColor`, `DockedColor`, `LaunchingColor` | The glow's colour in each state |

### Making a pad for big ships

Make a new scene the way `landing_pad_small.tscn` is made, rather than scaling the small one: its numbers are in metres and do not follow the node's scale.

1. A `Node3D` root with `scripts/Space/LandingPad.cs`. Set `MaxFootprint` and `MaxHeight` to the ships it is for (Synty's cruisers are 23 to 37 m wide and 50 to 52 m long). Give it a larger `Leeway` and `CaptureHeight`, and longer `SettleSeconds` and `LaunchSeconds`, to suit them.
2. The platform: any model, with a `Solid` (its collision mesh, or `BoxShape3D` children) so ships bump into it. Put its top at the root's origin.
3. The glow: a `Glow` child holding meshes that use `pad_glow.gdshader` (a cylinder rising from the pad, a ring round it) and a light. The shader's `height` should be the mesh's height. Make the column about as wide as the `Leeway`.
4. A `Marker3D` along -Z helps to see which way ships will park.
5. Put it in a hangar big enough for those ships. The station only clears a hangar for a ship if one of its pads fits the ship.

Pads only work inside a hangar with a `HangarDock`. Pads out in the open are not supported yet.

## Hull panels

`tools/station_bay/hull_panels.py` cuts slices of the ring's outer band (an angle and a height, `PANELS`), unrolls them flat and writes them as panels: 30 x 30 m, facing +Z, the skin at z = 0 and its plates standing out up to 4 m (0.6 m behind it). They have the ring's plates, colours and lit windows. A: light, B: white, C: blue. Scale them to fit; cover their edges with `hull_trim.tscn`.

`tools/station_bay/hangar_preview.tscn` (the hangar alone, with a cruiser and a fighter for scale) and `preview.tscn` (the station) take pictures with a `ViewShots` node into `user://` (they need a window).

## Solid

Every module and part has a `Solid` node: what ships bump into, and what stops rounds, missiles and drones. Its surface is made of triangles, taken from:

- `Shape`: a model whose meshes are the surface. For the station modules, Synty's collision meshes (`SM_Ship_Station_0N_Collision.fbx`: 92 to 1,136 triangles each, close to the hull but far fewer triangles than the visible model). They are not in the pack's source files: `tools/import_synty.cmake` takes them out of the Unity package.
- `CollisionShape3D` children with a `BoxShape3D`: for small parts with no collision mesh (the turrets and the tube have one box per piece).

A solid may be hollow (the triangles do not have to close round anything), so a hangar's walls, floor and ceiling can be a solid a ship flies into. A ship's hitboxes are tested against the triangles near it; it bounces off as it does off rocks, and the hull takes the energy the crash took. Solids do not move. If the node is turned or scaled in the editor its triangles follow. `CellSize` (25 m) is the size of the grid the triangles are filed in. It only changes how fast the tests are, never their result.

Solids do not yet block radar or seeker lines of sight. Rocks do.

## AvoidCapsule

Every module has an `AvoidCapsule`: a rough shape round it that AI pilots' lidars (`RockSensor`) keep clear of, as they keep clear of rocks. The pilot steers round the capsule, not round every spar. The capsule is not solid.

| Field | Meaning |
|---|---|
| `Radius` | The capsule's radius (m) |
| `Height` | The length of its axis between the centres of its two caps (m); 0 makes a sphere. The axis is the node's Y: turn the node to lay the capsule down |

Make the capsule hold the whole module. A pilot already inside one does not see it, so a ship that starts there is not trapped.

A ship that means to go inside the station (to dock) sets its sensor's `Entering` to the station's root node. The capsules below that node are then not seen, and only the solid walls are in its way.
