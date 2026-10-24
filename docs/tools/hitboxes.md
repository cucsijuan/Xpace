# Hitboxes: a ship's boxes from its collision mesh

Rounds and blasts hit a ship's box collision shapes (`Net.Hitbox`): the host
tests them in plain code against where the ship was when the shooter saw it.
A box may be turned (a wing's box lies along the wing), so a few boxes fit a
ship closely.

The **hitbox tool** makes them from a model's collision mesh. Synty's ships
come with one (`Models/Collision/SM_*_Collision.fbx` in the Unity package,
the mesh its prefabs give their `MeshCollider`): a set of convex blocks. The
tool cuts the mesh into its loose pieces and gives every piece the smallest
turned box that holds it (along the piece's principal axes, or along one of
its faces and that face's longest edge: a block's faces give its exact box).

```sh
dotnet build
godot --headless --path . res://tools/hitboxes/hitbox_tool.tscn -- COLLISION.fbx res://scenes/ships/NAME_hitboxes.tscn
```

The FBX can be anywhere: it is read directly, not imported. The collision
meshes are not among the pack's `SourceFiles`; take them out of the Unity
package (it is a tar.gz: every asset sits in a folder named by its GUID, with
its path in the `pathname` file).

The scene it writes is a `Node3D` of `CollisionShape3D` boxes in the model's
own frame. Put it beside the model in a ship's scene, with the same transform
as the model (Synty models face +Z: turned 180 degrees about Y). Only numbers
taken from the mesh are in it, so it goes in the repository, unlike the
model.

`scenes/ships/fighter_03_hitboxes.tscn` (SM_Ship_Fighter_03: 23 boxes,
129 m³ against 1,133 m³ for one box round the whole ship; every vertex of
the visible model within 25 cm of a box) is used by the player's ship in
`scenes/main.tscn` and by `scenes/ships/remote_fighter.tscn`, what other
players' ships look like: the boxes rounds hit are the ones the shooter sees.

Shapes of an area or body that is not on the rounds' layer (a cockpit
screen's, for the mouse) are not hitboxes.
