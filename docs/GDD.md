# Xpace: master design document

Version 0.3, 22 October 2026. A living document: sections marked **[open]** are still to be decided.

Codename: **Xpace**. The final name of the game and of the galaxy is pending (section 13).

---

## 1. The idea in one sentence

A space sandbox of trade, combat and factions, in the vein of X4, Elite Dangerous, Avorion and No Man's Sky, where **every ship's brain is a real 8086 computer**. Navigation, targeting, radar, radios, missiles and turrets all run on software on that CPU. Players can use the factory software, buy better programs, or write their own.

## 2. Pillars

1. **The computer is the ship.** Every digital system goes through an emulated CPU. It is not a decorative layer: better targeting software means more hits.
2. **Programming is optional, never required.** Every system ships with factory software that works well. Programming is a way to beat it, not a requirement to play.
3. **Combat with believable sensors.** Radar, infrared signatures, radar warning receivers (RWR), countermeasures and guided missiles as in a modern fighter jet, plus the science-fiction classics: lasers, railguns and shields.
4. **A universe with owners.** Corporations, factions and politics that exist without the player, with an economy that produces, transports and consumes.
5. **Be whatever you want.** Trader, miner, shipbuilder, software designer, politician or pirate.

## 3. Presentation and the player

### 3.1 Camera and cockpit (decided)

- **Always first person, from the cockpit.** There is no external flight camera.
- **The computer's screens are in the cockpit.** The multifunction displays (MFDs) show what the ship's CPU draws: CGA text, VGA graphics, the radar, the editor. What runs on the 8086 is physically in front of the pilot, which reinforces pillar 1.
- **Small ships (fighters):** the player only has the cockpit.
- **Large ships (future):** the player can get up from the seat and walk around the ship.

### 3.2 The player has a body

- There is a physical character, even if at first it is almost always seated in the cockpit.
- Walking around large ships and stations comes later. The architecture must allow it from the start: the character is an entity separate from the ship, which "sits" in it.

### 3.3 Art

- The **Synty POLYGON Sci-Fi Space** kit is used. It has ships and cockpits made for first person, without interiors, which is enough for now.
- Synty's low-poly style sets the art direction. Our own assets (screens, HUD, new stations) must follow it.
- Interiors for walking around large ships will need another kit or our own art later.

### 3.4 Death and insurance (decided)

- **No permadeath.**
- When destroyed, the player respawns at a station and gets the ship back by paying the **insurance**, a percentage of its value, as in Elite Dangerous.
- If the player cannot afford it, they get a basic ship. **[open]** Is the cargo lost? And the installed software, or is it kept in the player's account?

## 4. Universe and travel

### 4.1 Structure

- **Sector = a solar system.** Each sector has a star, planets, asteroid belts, stations, jump gates and anomalies.
- **Sector map:** a graph of hundreds of sectors, hand-made in the populated core and procedurally generated toward the frontier.
- **Scale:** inside a system, "playable", compressed distances (as in X4), not real ones (as in Elite), so a trip between planets takes minutes, not hours.
- **Planets:** no landing for now (decided). They are scenery and navigation references. What players visit are space stations and similar structures: shipyards, mining outposts, pirate bases, ruins. Planetary landing is for the future.

### 4.2 Travel between sectors: jumps (decided)

There are two ways to jump, and they coexist:

| Way | How it works | Why it exists |
|---|---|---|
| **Jump gates** | Fixed structures linking neighbouring sectors. Instant and safe, but owned: they charge tolls, ask for clearance and can be closed. | They create trade routes, choke points and politics. Controlling a gate is power. |
| **Jump drive** | Fitted to the ship. It needs fuel and a spool-up time that grows with mass. The coordinates are **computed by the navigation computer**. | Freedom, exploration and escape. Used by smugglers and pirates. |

Jump computation is a good example of the CPU as a mechanic: a better navigation program, or a faster CPU, computes the jump sooner or with less error. A badly computed jump leaves you far from the destination, or somewhere dangerous.

### 4.3 Travel inside a system: real time

- **Normal flight:** Newtonian, six degrees of freedom, for combat and manoeuvring.
- **Cruise:** a high-speed mode to cross the system. It drops out near large masses and can be interdicted.
- X4-style space highways are ruled out; gates and cruise cover that role.

## 5. The onboard computer

### 5.1 Hardware

- **CPU:** pc86's vm86 core, a faithful 8086 with 1 MB of memory, I/O ports and interrupts. It runs at a reference 4.77 MHz, about 400,000 instructions a second.
- **Expansion bus ("Xbus"):** every ship module is a device with its own port range: slot *n* uses ports `100h + n·16`. Since M1 there are three devices:
  - the pilot's controls, at `100h`;
  - the inertial unit, at `110h`, which raises IRQ 5 with every sample, 60 times a second;
  - the thruster controller, at `120h`, with a watchdog: if the software stops writing for 100 ms, it falls back to direct mode.

  The registers are documented in `software/flight/flight.asm`. The factory flight software (FLIGHT.COM, started from AUTOEXEC.BAT) uses 5% of the 4.77 MHz CPU.

  M2 added three more devices:
  - the radar, at `130h`: a ±60° cone out to 8 km, with the contact table and the track written to memory at D000:1000;
  - the gimballed turret, at `140h`: 90°/s servos and a 1,200 m/s kinetic cannon;
  - the display card, at `150h`: the cockpit's screens, listed in a table in memory (name, mode, size, where its memory is); programs find theirs by name and pick text (CGA-style cells) or graphics (2 bits a pixel, with phosphor persistence). Each screen is a `DisplayScreen` resource, so a cockpit can have any set of them.

  FLIGHT and FIRECTL stay **resident** and do their work inside the interrupt, so the central console stays free. FIRECTL computes the lead with CORDIC (angles and distances with no square root), turns its filter with the gyros so the ship's own rotation is not taken for target motion, and uses 33% of the CPU: fire control costs six times what flight control does. The CPU-time-as-a-resource mechanic is already working.

  **Realistic accuracy.** The radar measures positions only, with noise that comes from its signal-to-noise ratio (SNR):
  - the echo falls with the fourth power of the range and grows with the target's radar cross-section;
  - jammers raise the noise: they fall only with the square of their distance and count in full inside the beam, hence the *burn-through* range.

  FIRECTL estimates the velocity with an alpha-beta filter. On top of that come servo inertia, cannon dispersion (0.8 mrad, growing with barrel heat), drones that jink at 2 g once locked or hit, and hitboxes that are the ship's real shape. Against a manoeuvring target the cannon hits about 33% at 500 m, 10% at 1 km and almost nothing from 2 km; against a target flying straight, 46% at 2 km. An evading fighter has to be fought inside 1 km.

  A better radar (more power, a narrower beam), a better turret or better software (other filter gains, estimating acceleration, compensating the ship's own rotation, rejecting the jumps of a deception jammer) improves those numbers: that is where the equipment and software markets come in.
- **Hardware discovery:** register 0 of each slot gives the device type, the manufacturer and the version, as the HWQ instruction did in 0x10c. Software walks the bus to find out what is installed.
- **Interrupts:** devices report without the CPU having to ask: a new radar contact, an incoming missile, a radio message, damage.
- **Hardware upgrades:**
  - faster CPUs (8 MHz, 10 MHz, "Turbo");
  - more memory;
  - a **maths coprocessor** (the 8087, as a separate module), which makes floating point cheap;
  - **dedicated controllers**: extra CPUs, for example one per turret, to unload the main one.

**CPU time is a resource, like energy or heat.** An 8086 executes about 6,600 instructions per frame at 60 Hz. Keeping three turrets with lead computation, plus radar, plus navigation, does not fit in one cheap CPU. That forces a choice between buying hardware and optimising software.

### 5.2 Software

- **Operating system:** COS, CucsiCorp's DOS, which already exists in sw86. The ship boots from its "diskette" (in the game, a cartridge or a ROM).
- **In-game languages:** CBasic for beginners, C (Small-C: `.COM` and `.EXE` programs, several source files, libraries, `#asm` for the hot loops) for larger programs, and ASM for those who need speed. All three already exist, along with the EDIT editor.
- **Factory software:** each manufacturer sells its programs: flight control, targeting, electronic warfare, navigation and automated trading.
- **Software is a product:** it has versions, licences, bugs and patches. There is pirated software and a market of player-made programs (at first, inside the game itself).

### 5.3 Examples of what the CPU does

| System | What the software does |
|---|---|
| **Flight control (flight assist)** | Turns the stick into thruster firings, damps rotation and holds speed. It can be replaced or switched off. |
| **Gimballed turrets** | Read the target the radar has locked, compute the intercept point with lead and drive the servos. |
| **Radar-guided missiles** | The missile has its own microcontroller. It can receive updates from the mother ship's radar over a datalink (semi-active guidance) or switch on its own radar at the end (active, "fire and forget"). |
| **Infrared missiles** | The seeker gives an angle to the heat source and the software applies proportional navigation. Flares and the ship's heat signature come into it. |
| **Radar** | Search and track modes, and track-while-scan (TWS). Locks targets and ranks threats. |
| **Electronic warfare** | Jamming, decoys and analysis of the radar warning receiver (RWR). |
| **Communications** | Radio and modem to talk to stations, fleets and other ships. For trading, coordinating, and also **hacking**. |
| **Automation** | Trade and mining routes, and orders to ships of your own fleet. |

## 6. Combat

### 6.1 Sensors and signatures

- Every ship has a **radar signature** (from its size and hull) and an **infrared signature** (from the heat of its engines and weapons).
- **Stealth:** shutting systems down, cooling the ship and flying with the radar off (passive mode).
- **Radar warning receiver (RWR):** warns you when you are being searched, tracked, or fired at.

### 6.2 Weapons

| Type | Role |
|---|---|
| IR missiles | Short range, "fire and forget". Decoyed by flares. |
| Radar missiles | Medium and long range. Decoyed by chaff and jamming. |
| Lasers | Instant hit. They use a lot of energy and generate heat. |
| Railguns | A very fast, hard-hitting round. They need lead (the CPU) and charging. |
| Ballistic cannons | Cheap, use ammunition, good against hulls. |
| Torpedoes | Against capital ships. Slow and interceptable. |

### 6.3 Defences

Shields, armour, point defence systems and countermeasures: flares, chaff, jamming and decoys.

### 6.4 Energy and heat

- The reactor generates energy, which is shared between weapons, shields, engines and systems, managed live in the style of Elite's "pips".
- Firing, accelerating and jumping generate **heat**. Heat raises the infrared signature and, past a limit, damages modules.
- Switching systems on and off is part of the tactics, and can also be done from software.

## 7. Ship fittings

- **Slot sizes:** S, M, L and XL (capital ships).
- **Slot types:** weapons, utility, internal, and computer bus slots.
- **Constraints:** energy, heat, mass, bus slots and **CPU time**.
- **Factory hulls (decided):** every ship is a model from a manufacturer. There is no free block building as in Avorion.
- **Modular parts:** some hull parts can be swapped, such as wings, engine nacelles and cargo modules, if the Synty kit allows it. They change the looks and also the numbers (mass, signature, slots). **[open]** Which interchangeable parts the kit has still needs checking.

## 8. Economy, corporations and politics

### 8.1 Economy

- Stations produce from raw materials in production chains like X4's: ore → metal → components → modules → ships.
- The economy is simulated: prices from supply and demand, and trading ships that really exist and can be attacked.

### 8.2 Corporations

- Many companies sell hulls, modules, CPUs and software.
- Each has its own style and its manufacturer ID on the bus.
- **CucsiCorp** fits as the hardware and operating system giant.
- They compete: they launch products, have scandals and ship patches.

### 8.3 Factions and politics

- There are governments, corporations, guilds and pirates, with relations, territory and wars.
- **Reputation** with each faction.
- **Starting point (decided): Elite Dangerous's politics.**
  - Factions have influence in each sector, which rises or falls with players' actions: missions, trade, bounties and combat.
  - Factions have states: boom, war, elections, crisis, and so on.
  - Control of stations and gates changes when influence does.
- **Possible expansions [open]**, to be decided once the base works:
  - controlling stations and gates directly;
  - setting taxes and tolls;
  - forming alliances;
  - founding a faction of your own.

## 9. Player roles

| Role | Main loop |
|---|---|
| Trader | Buy low, transport, sell. Scales up to automated fleets running your own software. |
| Miner | Prospect, extract, refine. |
| Builder | Build stations and factories. Fit out and customise factory ships (modular parts, modules and software) and sell them. |
| Programmer or software corporation | Write programs and sell or license them. |
| Mercenary or bounty hunter | Combat contracts. |
| Pirate | Interdict, rob and hack. Ends up as a pirate empire. |
| Politician | Influence, territory and a faction of your own. |

## 10. Technology

- **Engine:** Godot 4.7 (.NET, C#).
- **CPU:** pc86 built as a native library (CMake, on Windows and Linux) and called from C# with P/Invoke. Its C API (`vm86.h`, `pc.h`) was designed for this:
  - no threads and no clock of its own: the engine decides how many instructions run per frame;
  - the complete state saves and restores as a block of bytes, which serves for saved games.
- **Devices:** the ship's bus is a small C card (`native/xbus.c`) whose registers the C# devices read and write between machine steps.
- **Simulation level of detail** (essential):
  - **near the player:** CPUs really run, instruction by instruction;
  - **far away:** AI ships use an abstract model ("level 3 factory software hits X%"), with no emulation;
  - **missiles:** emulated only if the player fired them or they are heading for the player.
- **Real PC CPU budget:** measured in M0, the full pc86 board executes about 107 million instructions a second on one core (a 2026 office PC, MSVC Release build). That is about **260 machines at 4.77 MHz per core**, plenty for the player's ship, its turrets and nearby missiles. Each instance reserves 1 MB today; missiles would suit a vm86 variant with little memory, 64 KB or less.
- **Spatial scale:** a floating origin (`FloatingOrigin`) moves the world back whenever the ship is 1 km from the origin, so single-precision floats never lose the millimetres the cockpit needs; Godot's double-precision build stays an option for very large sectors.
- **Tools:** asm86 (and Small-C, built for the PC) for the factory software; COS, ASM, CBasic and Small-C inside the game for players.

### 10.1 Networking (planned, after M3)

Multiplayer is co-op and drop-in PvP invasions, not an MMO: a small session (a host and a few guests) over Epic Online Services P2P, Device ID login. The design follows Glenn Fiedler's *Gaffer on Games* articles, picking what fits a game where every ship's brain is an emulated 8086.

**Why not lockstep or full rollback.** Deterministic lockstep needs the same physics result on every machine, and neither Godot Physics nor Jolt gives that across machines, compilers and operating systems (the game runs on Linux and Windows). Client-side prediction with rollback would mean rewinding and re-running the ship's pc86 (memory, CPU and devices) for every correction. vm86 is deterministic and could do it, but it costs a machine snapshot per tick and buys nothing the authority scheme below doesn't already give.

**Authority.**
- **Your own ship belongs to you.** Its computer and its physics run only on your machine, with no prediction and no corrections: it always answers the stick at once, with no input lag. This is the authority scheme of Fiedler's *Networked Physics*, and what Elite Dangerous does.
- **Other players' ships** are never emulated elsewhere. Their owner sends their state (position, orientation, velocity, angular velocity, tick number) 20 to 30 times a second, unreliable.
- **The host owns everything else:** drones, missiles with their microcontrollers, hits, damage and collisions between ships of different owners (it tells each owner the impulse to apply).

**Our own ship integrator.** The ships' motion is integrated by our code (`ShipMotion`), not by the physics engine: a ship in space is a rigid body under thrust and torque, with no gravity, drag or resting contacts, so the step is a few lines. That gives a fixed, known step, a pure function of (state, commands) that can be re-run if rollback is ever needed, and arithmetic made only of `+ - * /` and square roots, which is bit-identical on every x64 machine. The engine is kept for collision shapes and queries.

**What we take from Gaffer on Games.**

| Technique | Use |
|---|---|
| Fixed timestep and a shared tick | The simulation already runs at a fixed 60 Hz; the session adds a sector tick number every machine agrees on. |
| Snapshot interpolation | Remote ships are drawn about 100 ms in the past, interpolated with Hermite curves through position and velocity (nearly exact in space). |
| Extrapolation | Only to cover lost packets; with no drag it is very accurate. |
| State compression | Smallest-three quaternions, positions quantised relative to the sector, deltas against the last acknowledged snapshot, a priority accumulator when there are many entities. |
| Lag compensation | On the host, for hits (below). |
| Deterministic lockstep, prediction with rollback | Not used, see above. |

**Built so far** (`scripts/Net`, tested by `tests/net_test.tscn` over a simulated link): a 33-byte snapshot (660 bytes a second per ship at 20 Hz, before delta compression), the sector tick, and remote ships interpolated with an adaptive playout delay. Following an owner flying hard 5 g manoeuvres, the remote ship is off by 0.1 cm on average on a 100 ms line with 5% loss (shown 200 ms late), and 0.2 cm on bad wifi (150 ms, 20% loss, 266 ms late).

Rounds too (`tests/lag_test.tscn`): a 34-byte fire event (shooter, sequence, tick, origin, velocity, damage and how late the shooter showed each ship), a hit event, swept hit boxes tested in plain code (the ship's motion across the step counts) and the host's `LagCompensator`. Against a ship crossing at 200 m/s, the host confirms every hit the shooter saw and adds none, even with fire events 400 ms late; without compensation it confirms none (the target is 33 m further on by then). The shooter flies its own rounds from the event it sent, quantised the same way, so both sides fly the same round. The rewind is capped at 0.5 s: a shooter seeing its target later than that has to lead it more.

The session (`Session`, joined to the game by `SessionNode`) is star-shaped round the host, peer 0: each machine's own ship sends snapshots, the host relays them to the other guests, judges every round and sends the verdicts to everyone; the owner of the ship hit takes the damage. Drones are the host's, numbered by name the same way on every machine, and are puppets on the guests. With a session running, `Projectiles` fires through it, and rounds only fly for show until the host's hit event removes them. Tested with three machines in plain code (`tests/session_test.tscn`) and with the real scene as host beside a guest (`tests/session_scene_test.tscn`). The transport is an interface (`INetTransport`); EOS P2P is still to come.

**Rounds and hits.** Cannon rounds are not hitscan (1,200 m/s: 1.7 s to 2 km) and in space they fly straight, so a reliable fire event (tick, origin, velocity) lets every machine simulate the same round with no further traffic. The host keeps about a second of history of every ship and tests each round's segment against the target **as the shooter saw it**: now, minus the interpolation delay, minus half the shooter's round trip (Source-style lag compensation, favouring the shooter).

**Latency is a physical cause.** Missiles run on the host; a player's radar datalink reaches them across the network, so network latency becomes datalink latency. Likewise the radar and FIRECTL's filter see remote ships slightly late, which is sensor error, not dice: better hardware and software still make a measurable difference.

**Frames of reference.** Positions travel as sector-absolute doubles; the floating origin stays a local matter of each machine.

**Cheating.** With owner authority a player could lie about their ship. For co-op and invasions a plausibility check on the host is enough: speeds and accelerations the hull and its delta-v allow, rates of fire the turret allows. Only lobby members are accepted as P2P peers. There is no host migration: if the host leaves, the session ends.

## 11. Assessment and recommendations

**The strongest point.** The idea has something none of its references have: in X4 and Elite a ship is a spreadsheet of numbers; here the ship is a machine you can understand and modify. And much of the hardware and software already exists and works: the 8086, COS, the assembler, CBasic, the C compiler and EDIT. That is a huge advantage over 0x10c, which had to invent all of that.

**The main risk is scope.** What is described here is four games: a combat simulator, an X4, an Avorion and a 0x10c. 0x10c was cancelled precisely for trying to do too much. The recommendation is to build in layers, each playable on its own:

1. **One ship, one sector, combat.** Newtonian flight, radar, a missile and a turret, all controlled by factory software running on vm86. If this is not fun, nothing else will save it.
2. **Programming the ship.** An in-game terminal with COS, and the ability to change a program and see the difference in combat.
3. **Several sectors, jumps and simple trade.**
4. **Simulated economy, factions and corporations.**
5. **Building, politics and fleets.**

**On programming.**
- 95% of players will not program. The game must be good without programming, and programming must give visible but optional advantages.
- Factory software should have **configurable parameters**, for example through configuration files or a menu, so the step from "using" to "programming" is gentler.

**On the first-person cockpit.** It is the best decision for this game, because the cockpit screens are the natural place to show the computer. It has two technical consequences:
- Each cockpit screen is a texture updated from vm86's video. That is cheap: an 80×25 text mode or a 320×200 screen.
- Synty's cockpits probably have no surfaces meant as screens; MFD meshes will likely have to be added or adapted in each cockpit.

**On factory hulls.** The right choice for the scope: fixed hulls with modular parts give variety without the cost of a building system, and they give corporations a purpose, because every model has a manufacturer.

**On insurance.** It fits the economy well, but what happens to software needs deciding soon. If a program the player wrote is lost on death, programming becomes frustrating. Recommendation: software belongs to the player, not the ship; it stays saved and is reinstalled.

**On jumps.** Jumps are the right call: they give identity, as in Star Wars and Star Trek, and avoid modelling highways between systems. Combining owned gates with free jump drives gives both politics and freedom.

**On realism.** Fighter-jet combat (radar, signatures, countermeasures) is more interesting than full physical realism. Recommended: Newtonian flight with a software flight assist (which the CPU can improve), compressed distances, and speeds and ranges chosen so encounters last seconds or minutes, not hours.

**On multiplayer.** Start single-player. Emulating CPUs over a network, with its determinism and synchronisation, is a project of its own; the plan in 10.1 avoids it by running each ship's computer only on its owner's machine.

**On hacking other ships.** Very attractive, because modems and radios make it natural, but it can break the balance. It should be approached as protocols with concrete vulnerabilities that corporations patch, not as a "hack" button.

## 12. Roadmap

| Milestone | Content | Sign of success |
|---|---|---|
| M0 ✔ | vm86 as a native library inside Godot, a text console on screen, COS booting | `A:\>` on a cockpit screen |
| M1 ✔ | 6DOF flight with the flight assist running on the CPU | The ship handles well and the flight assist is a replaceable program |
| M2 ✔ | Radar, target lock, a gimballed turret controlled by software | The turret follows the target thanks to the program |
| M3 | IR and radar missiles, countermeasures, heat and energy | A 1–3 minute duel against the AI that is fun |
| M4 | Terminal in the cockpit: edit and rebuild software in the game | Changing the turret's program improves its aim |
| M5 | Several sectors, jumps, stations, trade | A trade run from end to end |
| M6+ | Economy, factions, corporations, building, politics | — |

## 13. Decisions and open questions

### Decided

| Topic | Decision |
|---|---|
| Camera | Always first person, from the cockpit. |
| Body | The player has a body. Fighters have only the cockpit; walking around large ships and stations is for the future. |
| Art | Synty POLYGON Sci-Fi Space: ships and first-person cockpits, no interiors for now. |
| Ships | Factory hulls, with modular parts if the kit allows it. |
| Politics | An Elite Dangerous-style base (influence and faction states), expanded later. |
| Planets | No landing for now; only stations and structures. Landing is for the future. |
| Death | No permadeath; Elite-style insurance. |
| Travel between sectors | Jumps: owned gates and the ship's own jump drive. |
| Name | Codename Xpace. |

### Pending

- **The final name of the game and of the galaxy.** To be decided soon.
- Which modular parts the Synty kit has, and how they show in the numbers.
- Insurance: what is lost on death (cargo, software)?
- How far politics goes beyond the Elite base.
