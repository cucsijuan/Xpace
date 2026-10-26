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
| `scenes/stations/props/steam_vent.tscn` | Synty's FX_Steam: grey puffs rising from a vent |

The modules fit together at their origins, as in the demo: ring, outer ring, inner ring and tower all at (0, 0, 0), the hub at (0, -115.5, 0), the keel at (0, -228.9, 0). The turrets are only looks for now: they do not aim or fire.

The station modules use `assets/materials/synty_station.tres`, Synty's `PolygonScifiSpace_SpaceStation_01` material: the 01_B colour atlas multiplied by the station's wear mask (`PolygonSciFiSpace_Ship_Mask_Station_01`, on the second UV channel) and the emissive map for the windows. To paint a station another colour, duplicate the material and change its albedo texture to another of Synty's atlases (`Textures/Alts` in the pack: 01_A to 04_F). Add the atlas to the `TEXTURES` list in `tools/import_synty.cmake`, which copies it in, and to the private art repository.

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
