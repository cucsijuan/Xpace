# Xpace

A space game (trade, combat, factions) where the ships' systems run on programmable 8086 computers. Codename.

- Engine: Godot 4.7 (.NET, C#).
- Design: [docs/GDD.md](docs/GDD.md).
- The ship's computer is the pc86 board (vm86 CPU, BIOS, VGA, keyboard, timer, diskette drive) running COS, CucsiCorp's DOS. Both come in as git submodules: [pc86](https://github.com/cucsijuan/pc86) and [sw86](https://github.com/cucsijuan/sw86), under `external/`.

## Building

Needs CMake 3.17 or later, a C compiler (Visual Studio 2022 on Windows, GCC or Clang on Linux), the .NET 8 SDK or later, and Godot 4.7 (.NET build).

```sh
git clone --recursive https://github.com/cucsijuan/Xpace.git
cd Xpace
cmake -S native -B native/build
cmake --build native/build --config Release
```

(In an existing clone without the submodules: `git submodule update --init`.)

That builds, from the submodules:

- `native/bin/pc86` (`pc86.dll`, `libpc86.so`): the machine.
- `native/bin/xpace_native`: the ship's bus card and text displays ([native/xbus.c](native/xbus.c)).
- `disks/cos.img`: the ship's diskette, with COS, `AUTOEXEC.BAT` and the ship's software: `FLIGHT.COM` (flight) and `FIRECTL.COM` (radar and turret), with their `.ASM` sources so they can be edited inside the game.

None of these go into the repository.

The art comes from Synty's POLYGON Sci-Fi Space pack, whose licence does not allow redistributing its files, so they are not in the repository either. With the pack's source files in `.raw-assets/SourceFiles` (or anywhere, passing `-DPACK=<path>`), copy the ones the game uses into `assets/synty/`, have Godot import them and cut the cockpit's sliders and switches out into meshes of their own (`assets/synty/parts/`, see [docs/tools/mesh-parts.md](docs/tools/mesh-parts.md)):

```sh
dotnet build
cmake -DGODOT=<path to the Godot .NET binary> -P tools/import_synty.cmake
```

Without `-DGODOT` it only copies the files. Then open the project in Godot (.NET).

## Controls

Elite Dangerous's keyboard and mouse controls. The middle mouse button switches between **flying** and **looking**.

| Action | Control |
|---|---|
| Take the mouse | Click (Esc gives it back, when not typing) |
| Pitch and roll | Mouse, while flying (a virtual stick: the ring in the middle shows it) |
| Throttle | W / S (it stays where it is left; below zero is reverse), X to zero |
| Yaw | A / D |
| Strafe sideways / up and down | Q / E, R / F |
| Flight assist | Z |
| Lock the target ahead / the next one | T / G |
| Fire | Left button, while flying |
| Look around | Mouse, while looking |
| Zoom in (to read the cockpit's screens) | Hold the right button |
| Type on the computer | While looking, click its screen; right-click lets go |
| Save / load the computer | F5 / F9 |

The arrow keys also pitch and roll, and a joystick or gamepad works with the same controls. Everything is in Godot's Input Map.

## The ship and its computer

Every physics step (60 a second), `ShipBody` writes the stick, the inertial unit's sample and what the other devices see into the bus registers, raises IRQ 5, runs the computer for 1/60 s, and applies what the software left written.

| Slot | Ports | Device |
|---|---|---|
| 0 | `100h` | Pilot's controls (stick, throttle, buttons) |
| 1 | `110h` | Inertial unit (raises IRQ 5 with every sample) |
| 2 | `120h` | Thrusters (100 ms watchdog: no commands, direct mode) |
| 3 | `130h` | Radar (contact table and track in memory at D000:1000; measures position only, with the noise its SNR allows) |
| 4 | `140h` | Gimballed turret with a 1,200 m/s kinetic cannon |
| 5 | `150h` | Display card: the cockpit's screens, found by name in a table at D000:1200; each shows text (CGA-style cells) or 2-bit graphics with phosphor persistence |

AUTOEXEC.BAT leaves two programs resident; they work inside the interrupt, so the console stays free:

- **FLIGHT.COM** (5% of the CPU): flight assist; its page goes on a FLIGHT screen, if the cockpit has one.
- **FIRECTL.COM** (34% of the CPU): locks targets with T/G, smooths the radar's noisy positions with an alpha-beta filter and gets the velocity from them (turning the filter with the gyros, so the ship's own rotation is not taken for target motion), computes the lead (CORDIC for angles and distances), aims the turret and fires while the trigger is held; draws a green B-scope on the RADAR screen (bearing across, range up, 2 km marks, the locked target in brackets) and a page on the WEAPON screen: target, range, lead, SNR, turret angles, barrel heat bar and status (GUN TOO HOT, OUT OF ARC when the target is where the turret cannot point).

A target lost at the edge of the radar's ±60° cone is taken back by itself if it comes back within 3 s.

**Why it misses.** Nothing is decided by dice: errors come from physical causes.
- **The radar:** its noise comes from its SNR, which falls with the fourth power of the range, grows with the target's signature and falls with jamming.
- **The filter:** its gains (`ALPHA`, `BETA_DT` in firectl.asm) trade smoothing the noise against following a turn in time.
- **The drones:** they jink at up to 2 g once locked or hit, and they are only as big as their shape: rounds through the space between fuselage and wings miss.
- **The servos:** they have inertia.
- **The cannon:** 0.8 mrad of dispersion, growing with barrel heat, and every round kicks the mount.

A better radar (more range, a narrower beam), a better turret or better software shows.

`FLIGHT OFF` and `FIRECTL OFF` take them out of memory (the last one installed first). The registers are documented at the top of [flight.asm](software/flight/flight.asm) and [firectl.asm](software/firectl/firectl.asm).

## Tests

With Godot's console executable (`godot` below):

```sh
godot --headless --path . res://tests/computer_test.tscn
godot --headless --fixed-fps 60 --path . res://tests/flight_test.tscn
godot --headless --fixed-fps 60 --path . res://tests/combat_test.tscn
godot --headless --fixed-fps 60 --path . res://tests/accuracy_test.tscn
godot --headless --fixed-fps 60 --path . res://tests/maneuver_test.tscn
```

- `computer_test`: boots COS, types `DIR`, saves and loads the computer, and measures how many 4.77 MHz machines one core runs.
- `flight_test`: the ship flown by FLIGHT.COM: turn rates and speed hold with flight assist, drift without it, the FA button, the console free while flying, and direct mode after `FLIGHT OFF`.
- `combat_test`: FIRECTL.COM against drones: locks with T, measures the aiming error against the exact intercept, counts the hits of a burst at 2 km, destroys the drone, switches target with G and turns the radar off with `FIRECTL OFF`.
- `accuracy_test`: hit probability by range against a drone jinking at 2 g (four runs per range), and a jammer on the drone: no lock at 4 km, burn-through at 1.5 km. It currently gives about 33% at 500 m, 10% at 1 km, 2% at 2 km and 0% from 4 km (46% against a straight target at 2 km): an evading drone dies inside 1 km, one flying straight even at 2 km.

- `maneuver_test`: firing while the ship turns (it keeps firing and hitting), the lock coming back after the target leaves the cone, and the floating origin moving the world back 40 km in the middle of a fight.

`tests/combat_demo.tscn` opens the game with a script at the controls (it locks and fires by itself), to watch or record.

All of them exit with 0 when everything passed.

## Layout

| Folder | Contents |
|---|---|
| `external` | The pc86 and sw86 submodules |
| `native` | The bus card and text drawing in C, and the CMake build of the libraries and the diskette |
| `scripts/Native` | P/Invoke over the pc86 and xpace_native libraries |
| `scripts/Computer` | `ShipComputer` (the ship's PC), `ShipBus` (the bus registers), the key map |
| `scripts/Ship` | `ShipBody` (physics, controls, inertial unit, thrusters, watchdog), radar, turret, the display card and its `DisplayScreen` resources, `ShipSpec`, `PilotControls` |
| `scripts/Cockpit` | The pilot (Elite-style controls, looking, picking screens), cockpit screens (placed in cockpit scenes, each picks what it shows), stick and throttle, the headset tones, the mouse widget |
| `scripts/Controls` | Buttons, sliders, switches, encoders: wired to what the ship exposes ([docs/controls.md](docs/controls.md)) |
| `scripts/Tools` | The mesh part tool ([docs/tools/mesh-parts.md](docs/tools/mesh-parts.md)) |
| `scripts/Space` | Stars, asteroids, space dust, the placeholder station, drones, rounds, the floating origin |
| `software` | The ship's software: `flight/flight.asm`, `firectl/firectl.asm`, `autoexec.bat` |
| `scenes` | `main.tscn`: the ship in the asteroid field; `cockpits/`: cockpit scenes (model, panels, the `Eye` marker for the camera); `panels/`: screens and controls grouped and wired; `controls/`: one scene per control, by kind (`sliders/`, `switches/`...); `weapons/`: missiles, chaff, flares |
| `assets` | `materials/`: shared materials; `ship/displays/`: the display screens (`DisplayScreen` resources); `synty/`: the Synty files (not in the repository) |
| `tools` | `import_synty.cmake`: copies the Synty files the game uses (and with `-DGODOT`, extracts the mesh parts); `mesh_parts/`: the mesh part tool's scene and its lists |
| `docs` | `GDD.md`: the game design; `controls.md`: controls and panels; `tools/`: the tools |
| `tests` | Computer, flight, combat and accuracy tests; the combat demo |
