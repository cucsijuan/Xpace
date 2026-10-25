# Controls and panels

Buttons, sliders, switches and knobs are scenes of their own; panels group
them with screens; cockpits (and later stations) place panels. Every
control is wired, in the inspector, to a property or a method that a
system exposes. Nothing here is specific to cockpits: a door button or a
light switch on a station works the same way.

```
scenes/controls/sliders/      one scene per kind of control, by category
scenes/controls/switches/
scenes/panels/cockpit/        panels: screens + controls, wired
scenes/cockpits/              the cockpit: the model, the panels, the eye
scripts/Controls/             the control classes
```

## Controls

A control's scene has the class on its root and the piece that moves
below it (`Part`). The control moves only that piece, in the scene's own
frame, so a control works at any angle on any panel.

| Class | Does | Moves the Part |
|---|---|---|
| `PushButton` | Calls `Method` with `Arguments`, or flips the bool `Toggle` | Sinks by `PressOffset` for `PressTime` |
| `SliderControl` | Sets the float `Property` between `Min` (bottom) and `Max` (top) | Along Y, from `Bottom` to `Top` metres off its modelled place |
| `ToggleSwitch` | Sets the bool `Property`, or calls `Method` with the new position | About X through `Hinge`, `OnAngle` / `OffAngle` degrees |
| `RotaryEncoder` | Adds `Step` to `Property` per detent (or calls `Method` with `UpArguments` / `DownArguments`) | About `Axis`, `DegreesPerStep` per detent |

All of them have:

- `Target`: what they work, **a path from the host**: the nearest
  `IControlHost` above them (the ship, `ShipBody`; a station later). So
  `Rwr/RwrTone` means the ship's warning receiver's audio, wherever the
  panel is. Sliders, switches and encoders follow their property: if
  something else changes it, the piece moves to match.
- `Radius`: the clickable sphere round the root.
- Glow (`GlowColor`, `GlowTint`, `GlowWidth`): the piece turns yellow while
  pointed at.
- `CutFrom` / `CutBox`: the old way, a piece carved out of a bigger mesh
  when the game starts (the reset button still does it). Prefer a scene
  made with the [mesh part tool](tools/mesh-parts.md).

The player works them while looking around (middle button: the dot in the
middle of the view points) or with the pointer (Alt: the cursor points, the
head stays still): click, drag a slider up and down or an encoder sideways,
or turn the wheel over them.

## What the ship exposes

| Path | Member | Kind |
|---|---|---|
| `ShipComputer` | `Reset()`, `PowerOn()`, `PowerOff()` | methods |
| `Display` | `PressKeyOn(screen, key)`: a bezel key for a screen (`"RWR"`, 1 zoom in, 2 zoom out, 3 sound) | method |
| `Rwr` | `Powered` | bool |
| `Rwr/RwrTone` | `Volume` (0 to 1) | float |
| `Radar` | `Powered` | bool |

Any exported property or public method of a node under the ship can be
wired the same way; these are the ones meant for it.

## A new control

1. Get the meshes: a part of a model (the [mesh part
   tool](tools/mesh-parts.md)) or your own.
2. New scene in the right category (`scenes/controls/<kind>/`): root a
   `Node3D` with the class, the fixed meshes and the moving one below it;
   set `Part` to the moving one.
3. Set the travel (`Bottom`/`Top`, `Hinge` and angles...) from the mesh's
   bounds (the tool's report gives them in the part's frame).
4. Leave `Target` and the member empty: the panel that uses it wires it.

## A new panel

1. New scene in `scenes/panels/<where>/`, root a plain `Node3D`: its frame
   is the panel's face (Y up along it, Z out of it toward the player).
2. Add its screens (`CockpitScreen`, Source Display and the screen's
   name), its housing, and instances of controls.
3. Wire each control: `Target` (a path from the ship) and the member.
4. Place the panel in the cockpit scene.

`scenes/panels/cockpit/rwr_panel.tscn` (the RWR screen and its volume
slider) and `radar_panel.tscn` (the radar and weapon screens, the radar's
and the RWR's power switches) are the examples.
