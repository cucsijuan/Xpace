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

### Exporting

With Godot's export templates (4.7.2, .NET) installed, after the native build and the art import:

```sh
godot --headless --path . --export-release "Linux" build/linux/Xpace.x86_64
godot --headless --path . --export-release "Windows Desktop" build/windows/Xpace.exe
```

The presets (`export_presets.cfg`) put the diskettes (`disks/*.img`, `disks/*.bin`) in the game's pack. The native libraries go into the data folder beside the executable (`data_Xpace_<platform>_x86_64/`) with the assemblies, and so does the EOS SDK's library when the build has online play. An export takes the native libraries in `native/bin` for its platform. They are built for the system the export runs on, so export the Windows build on Windows (or copy the Windows `.dll`s into `native/bin`), or it starts without its computers.

Releases are built by `.github/workflows/release.yml`. Pushing a tag such as `v0.1.0`, which must match `config/version` in `project.godot`, exports both builds and publishes them as a GitHub release. Running the workflow by hand only keeps the zips as artifacts. The Windows libraries are built on a Windows runner, everything else on Linux.

The EOS SDK and the Synty files can't be in this repository, so the workflow clones them from private repositories (`cucsijuan/eos-sdk`, `cucsijuan/xpace-art`, the latter holding what `tools/import_synty.cmake` puts in `assets/synty`). It needs these secrets:

- `PRIVATE_REPOS_TOKEN`: read access to both of those repositories.
- `EOS_PRODUCT_ID`, `EOS_SANDBOX_ID`, `EOS_DEPLOYMENT_ID`, `EOS_CLIENT_ID`, `EOS_CLIENT_SECRET`: the game's product keys.

A tag fails without them. A run by hand still builds, with a warning, but has no online play or no art.

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
| Cooling boost (hold: the main engine keeps up more thrust; its radiators glow brighter) | Tab |
| Safe turns (full stick only asks what the thrusters can follow at this speed) | B |
| Lock the target ahead (again: let it go) / the next one | T / G |
| Fire | Left button, while flying |
| Next weapon (gun, radar missiles, heat seekers, pulse laser) | N |
| Chaff / flare | C / V |
| Heat sink | H |
| Jammer (off, noise, deception) | U |
| A pip to systems / engines / weapons, even them out | Left / Up / Right, Down |
| Look around / look ahead | Middle button (tap, hold or twice, see above) |
| Pointer | Alt (tap or hold) |
| Work a screen, button, slider or knob | Click it, while looking (the dot) or with the pointer; drag sliders and knobs, or turn them with the wheel |
| Zoom in (to read the cockpit's screens) | Hold the right button; the wheel zooms in steps |
| Type on the computer | Click its screen; right-click lets go (meanwhile every key is the computer's, Esc too) |
| Menu (resume, settings, main menu, quit) | Esc; alone it pauses the game, online the game goes on |
| Save / load the computer | F5 / F9 |

I / K pitch and J / L roll from the keyboard, and a joystick or gamepad works with the same controls. Everything can be bound again in the settings (Keys).

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
| 10 | `1A0h` | Thruster ratings: the accelerations the thrusters and the main engine give this ship; the whole plate at D000:1400 adds its top speeds and top turn rates (where the thrust meets the veil) |
| 11 | `1B0h` | Power plant and distributor: three capacitors (systems, engines, weapons) filled by twelve half pips of the plant's output |
| 12 | `1C0h` | Shield generator: takes hits before the hull, builds back from the systems capacitor, collapses when knocked down |
| 13 | `1D0h` | The ship's heat: plant waste and laser pulses in, radiators out; over 100% the hull cooks; heat sinks |
| 14 | `1E0h` | Radar jammer: noise (all round or aimed), and on the better models deception (range gate pull-off); draws from the systems capacitor, heats the ship, is heard by warning receivers |
| 15 | `1F0h` | Main engine: its own controller (a bare 8086 with firmware in ROM) and cooling unit; core temperature as the controller reckons it, coolant, cut-out, the cooling boost, a diagnostic port into the controller's ROM and a service port |

AUTOEXEC.BAT leaves four programs resident; they work inside the interrupt, so the console stays free:

- **FLIGHT.COM** (5% of the CPU): flight assist; its page goes on a FLIGHT screen, if the cockpit has one. Full stick and full throttle ask for the ship's own tops, from its rating plate: no top speed or turn rate is written in the program, so better thrusters or a stronger engine make the same software fly faster and turn harder. Each axis pushes what holds its setpoint against the veil at once and corrects the rest. Safe turns (B) hold full stick's pitch and yaw to what the thrusters can bend the ship's path to at its speed (from the rating plate), so the ship goes where it points: gentle turns fast, sharp ones slow, sharper with better thrusters.
- **FIRECTL.COM** (34% of the CPU): locks targets with T/G, smooths the radar's noisy positions with an alpha-beta filter and gets the velocity from them (turning the filter with the gyros, so the ship's own rotation is not taken for target motion), computes the lead (CORDIC for angles and distances), aims the turret and fires while the trigger is held; draws a green B-scope on the RADAR screen (bearing across, range up, 2 km marks, the locked target in brackets) and a page on the WEAPON screen: target, range, lead, SNR, turret angles, barrel heat bar and status (GUN TOO HOT, OUT OF ARC when the target is where the turret cannot point). With the pulse laser selected it fires the laser while the trigger is held, and works out the same solution for the laser's bolts: the lead point goes to the laser's sight and the head-up display draws a circle there, with a pipper where the pulses go at that range; put the pipper on the circle.

- **RWR.COM**: draws the warning receiver's picture on the RWR screen, and works the dispenser: one bundle of chaff or one flare on each press of C or V, with what is left in the bottom right corner (C chaff, F flares). It also works the jammer: U steps it off, noise, deception (if the model has it), off (JAM N / JAM D top right), and it aims the jammer's beam at the radar that has the ship locked.
- **POWER.COM**: works the power distributor from the arrow keys as in Elite (a pip to one takes half a pip from each of the others; at most four on one; Down evens them out) and throws a heat sink on H.

**Power, shields and heat.** The thrusters, the shields and the laser draw from their capacitors: with too small a share the shields build back slowly, the thrusters push less, the laser waits. What the plant makes and the laser fires heats the ship; the hotter it is, the brighter its hull on a heat seeker, and past 100% the hull wears away (shields don't stop that). A heat sink takes the heat away for a few seconds, and the hull's glow with it. The head-up display shows the pips and capacitors, the shield and the heat bottom right.

**The drive.** Ships fly on their generator: the main engine throws its thrust against the *veil*, the field that holds on to the sector's thin medium, and the veil pulls back with the square of the speed, so full thrust tops out at about 400 m/s (flight assist cruises at 250, with the engine pushing to hold it); coasting, a ship slows by itself, but far more slowly than its thrusters brake. Missiles are rockets and feel none of it. The main engine has its own controller: it gives what the throttle asks until it reckons the core too hot, then cuts the engine out and lets it pulse, keeping up only what the cooling carries away (full throttle gives the rating for about 20 s on an even share of power). More power to the engines drives the cooling harder; the cooling boost (Tab, held) harder still, for a brighter glow on heat seekers. The head-up display's DRV bar shows the core as the controller reckons it, marked at the cut-out. The controller is hardware and can't be switched off, but it was built to a price: it trusts the coolant reading the cooling unit puts on the bus (which has a calibration mode), it only looks at the core four times a second, and it has a bench mode no manual mentions. Getting more out of the engine that way costs it: the real core runs hot and wears, thrust beyond what the hull is rated for breaks the structure, and in the end the engine fails and takes the ship with it. Engines are `EngineSpec` resources (`assets/ship/engines`). The veil drags on turning too, so every ship has a top turn rate about each axis. Nothing about how fast a ship goes or turns is written in software: a hull (`ShipSpec`, `assets/ship/hulls`: mass, inertia, the veil's drag on moving and turning, what the structure takes), its manoeuvring thrusters (`ThrusterSpec`, `assets/ship/thrusters`) and its engine give it all, and the rating plate tells the programs. The veil drags harder across the hull than along its nose (`VeilCross`), as air would: a slide bleeds off and the path follows the nose, more the faster the ship goes. Safe turns let the nose lead the path by a few degrees, so the thrusters across, the main engine and the veil all bend it: the fighter turns about 52 deg/s at 150 m/s, 42 at 250 and 40 at 380, and a hard turn bleeds speed (380 m/s down to about 300 in three seconds). Every field of these resources, what it does and how to tune tiers with them: [docs/ships.md](docs/ships.md).

A target lost at the edge of the radar's ±60° cone is taken back by itself if it comes back within 3 s, near where the tracker lost it.

**Why it misses.** Nothing is decided by dice: errors come from physical causes.
- **The radar:** its noise comes from its SNR, which falls with the fourth power of the range, grows with the target's signature and falls with jamming.
- **Jammers:** noise falls only with the square of the range, so a jammed ship is seen only nearer (*burn-through*), and aiming the jammer's beam at one radar hides it from that radar further out; but the jammer is heard from far beyond that, and its noise shows on the radar as a strobe at its bearing. A deception jammer copies a tracking radar's echo, stronger, and walks the copy away: the track and any missile on its datalink go with it, until the copy stops and the lock is lost; close in the real echo wins. Models are `JammerSpec` resources (`assets/ship/jammers`: NJ-1 noise all round, NJ-2 aimed noise, DJ-3 noise and deception).
- **The filter:** its gains (`ALPHA`, `BETA_DT` in firectl.asm) trade smoothing the noise against following a turn in time.
- **The drones:** they jink at up to 2 g once locked or hit, and they are only as big as their shape: rounds through the space between fuselage and wings miss.
- **The servos:** they have inertia.
- **The cannon:** 0.8 mrad of dispersion, growing with barrel heat, and every round kicks the mount.

A better radar (more range, a narrower beam), a better turret or better software shows.

`FLIGHT OFF`, `FIRECTL OFF`, `RWR OFF` and `POWER OFF` take them out of memory (the last one installed first). The registers are documented at the top of [flight.asm](software/flight/flight.asm) and [firectl.asm](software/firectl/firectl.asm).

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
- `drive_test`: the veil's top speed and coasting; the main engine's controller cutting out and pulsing, half throttle never cutting, the cooling boost; the diagnostic port; the three flaws (calibration, timing between the controller's looks, the bench mode and its trim), the wear they cost, the hull breaking past its rating and the engine failing.
- `jammer_test`: RWR.COM's jammer button and aiming; noise hiding a ship far out, its strobe, burn-through close in, the aimed beam, a dry capacitor, the heat; a drone's radar; deception pulling a track 1.2 km away and breaking the lock, and failing close in; the jammer in snapshots.
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
