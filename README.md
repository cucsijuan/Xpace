# Xpace

A space game (trade, combat, factions) where the ships' systems run on programmable 8086 computers. Codename.

- Engine: Godot 4.7 (.NET, C#).
- Design: [docs/GDD.md](docs/GDD.md).
- The ship's computer is the pc86 board (vm86 CPU, BIOS, VGA, keyboard, timer, diskette drive) running COS, CucsiCorp's DOS. Both come in as git submodules: [pc86](https://github.com/cucsijuan/pc86) and [sw86](https://github.com/cucsijuan/sw86), under `external/`.
- Online play goes through Epic Online Services with [godot-eos](https://github.com/cucsijuan/godot-eos), also a submodule.

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

Elite Dangerous's keyboard and mouse controls, with three ways to use the mouse; the bar at the bottom of the screen says which one is on and the keys to change it:

- **Flying:** the mouse is the stick.
- **Looking** (middle button): the mouse turns the head and the dot in the middle works what it is on. A tap stays in it until the next tap, which flies again with the head left where it is (to fly watching a screen); held, it is a peek, and letting go looks ahead again. Two taps put the head ahead.
- **Pointer** (Alt): the cursor is free and the head stays where it was; point at screens and controls and work them. A tap stays until the next; held, it lasts while it is held.

The keys fly the ship in every mode, except while typing on the computer.

| Action | Control |
|---|---|
| Take the mouse | Click (when the window had lost it) |
| Pitch and roll | Mouse, while flying (a virtual stick: the ring in the middle shows it) |
| Throttle | W / S (it stays where it is left; below zero is reverse), X to zero |
| Yaw | A / D |
| Strafe sideways / up and down | Q / E, R / F |
| Flight assist | Z |
| Safe turns (full stick only asks what the thrusters can follow at this speed) | B |
| Lock the target ahead (again: let it go) / the next one | T / G |
| Fire | Left button, while flying |
| Next weapon (gun, radar missiles, heat seekers, pulse laser) | N |
| Chaff / flare | C / V |
| Look around / look ahead | Middle button (tap, hold or twice, see above) |
| Pointer | Alt (tap or hold) |
| Work a screen, button, slider or knob | Click it, while looking (the dot) or with the pointer; drag sliders and knobs, or turn them with the wheel |
| Zoom in (to read the cockpit's screens) | Hold the right button; the wheel zooms in steps |
| Type on the computer | Click its screen; right-click lets go (meanwhile every key is the computer's, Esc too) |
| Menu (resume, settings, main menu, quit) | Esc; alone it pauses the game, online the game goes on |
| Save / load the computer | F5 / F9 |

The arrow keys also pitch and roll, and a joystick or gamepad works with the same controls. Everything can be bound again in the settings (Keys).

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
| 6 | `160h` | Missile launcher with the radar missiles' datalink, and heat seekers with a head on the rail |
| 7 | `170h` | Radar warning receiver: the radars lighting the ship, and the plumes of missiles coming |
| 8 | `180h` | Countermeasures dispenser: chaff and flares, a limited load of each |
| 9 | `190h` | Pulse laser fixed along the nose (2,000 m/s bolts, heat), with the gunsight's lead point the program writes for the head-up display |
| 10 | `1A0h` | Thruster ratings: the accelerations the thrusters give this ship, read by FLIGHT's safe turns |

AUTOEXEC.BAT leaves three programs resident; they work inside the interrupt, so the console stays free:

- **FLIGHT.COM** (5% of the CPU): flight assist; its page goes on a FLIGHT screen, if the cockpit has one. Safe turns (B) hold full stick's pitch and yaw to what the thrusters can bend the ship's path to at its speed (from the rating plate), so the ship goes where it points: gentle turns fast, sharp ones slow, sharper with better thrusters.
- **FIRECTL.COM** (34% of the CPU): locks targets with T/G, smooths the radar's noisy positions with an alpha-beta filter and gets the velocity from them (turning the filter with the gyros, so the ship's own rotation is not taken for target motion), computes the lead (CORDIC for angles and distances), aims the turret and fires while the trigger is held; draws a green B-scope on the RADAR screen (bearing across, range up, 2 km marks, the locked target in brackets) and a page on the WEAPON screen: target, range, lead, SNR, turret angles, barrel heat bar and status (GUN TOO HOT, OUT OF ARC when the target is where the turret cannot point). With the pulse laser selected it fires the laser while the trigger is held, and works out the same solution for the laser's bolts: the lead point goes to the laser's sight and the head-up display draws a circle there, with a pipper where the pulses go at that range; put the pipper on the circle.

- **RWR.COM**: draws the warning receiver's picture on the RWR screen, and works the dispenser: one bundle of chaff or one flare on each press of C or V, with what is left in the bottom right corner (C chaff, F flares).

A target lost at the edge of the radar's ±60° cone is taken back by itself if it comes back within 3 s.

**Why it misses.** Nothing is decided by dice: errors come from physical causes.
- **The radar:** its noise comes from its SNR, which falls with the fourth power of the range, grows with the target's signature and falls with jamming.
- **The filter:** its gains (`ALPHA`, `BETA_DT` in firectl.asm) trade smoothing the noise against following a turn in time.
- **The drones:** they jink at up to 2 g once locked or hit, and they are only as big as their shape: rounds through the space between fuselage and wings miss.
- **The servos:** they have inertia.
- **The cannon:** 0.8 mrad of dispersion, growing with barrel heat, and every round kicks the mount.

A better radar (more range, a narrower beam), a better turret or better software shows.

`FLIGHT OFF`, `FIRECTL OFF` and `RWR OFF` take them out of memory (the last one installed first). The registers are documented at the top of [flight.asm](software/flight/flight.asm) and [firectl.asm](software/firectl/firectl.asm).

## The menu and settings

The game opens on its main menu: Single Player, Multiplayer (online, below), Settings and Quit. The settings are kept in `user://settings.cfg` and applied when the game starts:

- **General:** the pilot's name, which other players see.
- **Video:** resolution (the window's size when windowed; in the full-screen modes the 3D view is drawn at it and scaled to the screen), window (windowed, borderless, fullscreen), vertical sync, antialiasing (off, FXAA, MSAA 2x/4x/8x, TAA).
- **Controls:** invert the mouse's pitch, the mouse stick's and looking around's sensitivity.
- **Keys:** every flight action's key or mouse button (click it, then press the new one; Esc leaves it as it was). A key taken from another action leaves it unbound; game controllers keep their own bindings. Defaults puts everything back.

## Playing together

**Multiplayer** in the menu plays over the internet through Epic Online Services, with no ports to open: it joins the first game with room, and with none it hosts one, which the next player to choose Multiplayer joins. The game connects the players peer to peer (directly, or through Epic's relays), and only the lobby's members get in. A game whose host stopped answering (its game crashed) is skipped. A game just made takes a few seconds to show in searches, so Multiplayer looks three times over about six seconds before hosting, and a new host looks once more five seconds later: if two players made games at the same moment, one of them leaves its own and joins the other's.

On a LAN, or over the internet with the port open on the host (UDP 24680 by default):

```sh
godot --path . -- --host [port]                 # the host
godot --path . -- --join address[:port]         # every other player
```

With these, or with `--online` (what Multiplayer does), the game skips the menu.

Online play needs the EOS C# SDK next to the repository (`../eos-sdk/SDK`, or `-p:EosSdkDir=...`) when building; without it the game builds without online play. It also needs the product's keys, which are not in the repository: `python3 tools/eos_keys.py keys.json` writes them into the build (or set `EOS_PRODUCT_ID`, `EOS_SANDBOX_ID`, `EOS_DEPLOYMENT_ID`, `EOS_CLIENT_ID` and `EOS_CLIENT_SECRET` before starting the game). Players sign in with the computer's device account, and others see them by the pilot's name in the settings (`XPACE_NAME` overrides it). For several players on one computer, add `--eos-new-device` to every player but the first (`godot --path . -- --eos-new-device` opens the menu with it). `XPACE_EOS_LOG=1` shows the SDK's detailed log.

Each player's ship and its computer run only on that player's machine; the host flies the drones and judges every hit. Missiles fly on the machine that fired them; chaff, flares and radar locks are seen by everyone. How it works: [docs/GDD.md](docs/GDD.md), section 10.1.

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
- `flight_test`: the ship flown by FLIGHT.COM: turn rates and speed hold with flight assist, drift without it, the FA button, safe turns (at 150 m/s full stick slides the nose 80 degrees off the path without them, a few with them, and twice the thrust turns twice as hard), the console free while flying, and direct mode after `FLIGHT OFF`.
- `combat_test`: FIRECTL.COM against drones: locks with T, measures the aiming error against the exact intercept, counts the hits of a burst at 2 km, destroys the drone, switches target with G and turns the radar off with `FIRECTL OFF`.
- `accuracy_test`: hit probability by range against a drone jinking at 2 g (four runs per range), and a jammer on the drone: no lock at 4 km, burn-through at 1.5 km. It currently gives about 33% at 500 m, 10% at 1 km, 2% at 2 km and 0% from 4 km (46% against a straight target at 2 km): an evading drone dies inside 1 km, one flying straight even at 2 km.

- `hull_test`: rounds and a blast wear the hull away (its own rounds do not), the wreck drifts with nothing on the sensors, and the insurer's new ship comes back full.
- `duel_test`: the game's fighter, with its own computer, locks a drone, hits it with its laser (pointing the nose at its computer's lead point) and fires its radar missile and a heat seeker; breaks and throws flares against a heat seeker; and comes back after it is blown up.
- `raider_hunt_test`: in a session, the host's fighter and a drone find a guest's ship with their radars (never each other), the fighter's rounds hit it, judged by the host, and its missile is made on the guest's machine.
- `pilot_modes_test` (no `--fixed-fps` needed): the mouse's modes in the game's scene, from input events: looking with a tap, a hold and two taps, the pointer, and Esc's menu pausing the game alone and letting the keys go.
- `eos_again_test` (needs the product's keys and the internet; without keys it passes saying so): online play twice in one run, as going back to the main menu and choosing Multiplayer again.
- `menu_test` (no `--fixed-fps` needed): the main menu and its settings: every option filled, rebinding a key (taken from the action that had it, Esc to cancel, controllers untouched), changes applied at once and saved by Back, Defaults.
- `laser_test`: next weapon selects the pulse laser; locked, the lead point FIRECTL writes is within a few mrad of where a pulse meets the drone; fired on the lead circle the pulses hit a crossing drone, fired on the drone itself they pass behind it; held down it overheats.
- `maneuver_test`: firing while the ship turns (it keeps firing and hitting), the lock coming back after the target leaves the cone, and the floating origin moving the world back 40 km in the middle of a fight.

- `rock_test`, `avoid_test`: rock fields (the same rocks on every machine, ships bouncing off them, rounds stopped, radars blind behind them and lost in their clutter), and drones and the raider crossing a thick field with no rock sensor, a poor one and a good one.
- `missile_test`, `rwr_test`: radar and heat-seeking missiles against drones that break and drop chaff and flares; the warning receiver, its screen, and the dispenser worked by RWR.COM.
- Networking (no `--fixed-fps` needed): `net_test` (snapshots and interpolation), `lag_test` (lag compensation), `session_test`, `session_scene_test`, `udp_test` (real sockets on the loopback), `star_test` (the transport online play uses: only admitted players get in, another version is turned away, leaving the lobby leaves the session, a player silent for 10 s is dropped), `net_missile_test` (missiles, blasts, chaff, flares and radar locks across a session).

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
| `scripts/Menu` | The main menu, the settings screen and `GameSettings` (an autoload: the player's settings, applied at start) |
| `scripts/Tools` | The mesh part tool ([docs/tools/mesh-parts.md](docs/tools/mesh-parts.md)) |
| `scripts/Space` | Stars, asteroids, space dust, the placeholder station, drones, rounds, the floating origin |
| `software` | The ship's software: `flight/flight.asm`, `firectl/firectl.asm`, `autoexec.bat` |
| `scenes` | `menu/`: the main menu and the settings screen; `main.tscn`: the ship in the asteroid field; `cockpits/`: cockpit scenes (model, panels, the `Eye` marker for the camera); `panels/`: screens and controls grouped and wired; `controls/`: one scene per control, by kind (`sliders/`, `switches/`...); `weapons/`: missiles, chaff, flares |
| `assets` | `ui/`: the menus' theme; `materials/`: shared materials; `ship/displays/`: the display screens (`DisplayScreen` resources); `synty/`: the Synty files (not in the repository) |
| `tools` | `import_synty.cmake`: copies the Synty files the game uses (and with `-DGODOT`, extracts the mesh parts); `mesh_parts/`: the mesh part tool's scene and its lists |
| `docs` | `GDD.md`: the game design; `controls.md`: controls and panels; `tools/`: the tools |
| `tests` | Computer, flight, combat and accuracy tests; the combat demo |
