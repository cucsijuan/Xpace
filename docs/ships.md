# Ships: hulls, thrusters, engines and jammers

How a ship flies, how fast it goes, how hard it turns and how its engine and jammer behave all come from four kinds of resource (`.tres` files). No program on the ship's computer holds a top speed or a turn rate: FLIGHT reads them from the ship's rating plate, which is worked out from these resources. Change a number in the editor and the same software flies the ship differently.

| Resource | Script | Folder | Fitted where |
|---|---|---|---|
| Hull | `ShipSpec` | `assets/ship/hulls` | `Spec` on the ship's root (`ShipBody`) |
| Manoeuvring thrusters | `ThrusterSpec` | `assets/ship/thrusters` | `Thrusters` inside the hull resource |
| Main engine | `EngineSpec` | `assets/ship/engines` | `Spec` on the ship's `Engine` node (`EngineDevice`) |
| Jammer | `JammerSpec` | `assets/ship/jammers` | `Spec` on the ship's `Jammer` node (`JammerDevice`) |

The player's ship is `Ship` in `scenes/main.tscn`; the AI fighter is `scenes/ships/ai_fighter.tscn`. Both use `hull_fighter.tres`, `engine_d50.tres`; the player carries the DJ-3 jammer and the AI the NJ-2.

To make a new tier: duplicate a `.tres` in the editor (or copy the file), change its numbers and its `Model` name, and fit it on a ship. A new jammer model must also be added to the `Jammers` list of `SessionNode` in `main.tscn`, so other players' machines know it in a network session.

## The physics in one paragraph

Ships fly on their generator: the engines push against the **veil**, a field that holds on to the sector's thin medium, and the veil pulls back with the square of the speed, like air. So every direction has a **top speed**, where the push meets the drag, and every axis of turning has a **top turn rate**. The veil grips much harder across the hull than along its nose, so a slide bleeds off and the path follows the nose, more the faster the ship goes; that is what lets a ship turn without sliding. Missiles are rockets and feel none of it.

The formulas the rating plate uses (and that you can use to tune):

| What | Formula |
|---|---|
| Acceleration that way | push / mass |
| Top speed that way | sqrt(acceleration / veil drag that way) |
| Turning acceleration | torque / inertia about that axis |
| Top turn rate | sqrt(turning acceleration / veil spin about that axis) |
| Coasting from v, the time to halve the speed | 1 / (veil drag x v) |

## Hull (`ShipSpec`)

| Field | Units | What it does |
|---|---|---|
| `Mass` | kg | Divides every push: a heavier hull accelerates, strafes and tops out slower with the same engine and thrusters. |
| `Inertia` | kg m² | About the ship's X (pitch), Y (yaw), Z (roll). Divides the thrusters' torques: a bigger moment turns slower. |
| `Thrusters` | `ThrusterSpec` | The manoeuvring thrusters fitted. Empty: the plain thrust fields of this resource are used (tests do this). |
| `VeilDrag` | 1/m | The veil along the nose. Sets the top speed with the engine: 3.1e-4 with 50 m/s² gives about 400 m/s. Higher: slower ship, and it slows by itself sooner. |
| `VeilCross` | 1/m | The veil across the hull: X sideways, Y up and down. This is how **aerodynamic** a hull is: higher grips the medium harder, so slides die faster and safe turns are tighter (the more so the faster the ship goes); it also caps strafing speed, and a hard turn bleeds more speed. A fighter's is high, a freighter's low. |
| `VeilSpin` | 1/rad | The veil against turning, about X, Y, Z. With the thrusters' torques it sets the top turn rates. Higher: lower top rate. |
| `RatedAcceleration` | m/s² | What the structure takes along the nose. Only the main engine checks it: an engine pushed (hacked) past it damages the hull. A stock engine fitted to a hull stays under it. |
| `StressDamagePerSecond` | points/s | Hull damage at twice the rated acceleration (proportional below that). |
| `ThrustMegawatts` | MW | What the thrusters draw from the engines capacitor at full: the main engine all of it, each strafe half, each turn a quarter. |
| `MainThrust` | N | Only for a ship with no engine device (and the scale the thruster commands are worked in). |
| `ReverseThrust`, `LateralThrust`, `VerticalThrust`, `PitchAcceleration`, `YawAcceleration`, `RollAcceleration` | N, rad/s² | The plain thrusters, used only when `Thrusters` is empty. |
| `Restitution`, `Friction` | 0..1 | How it bounces off rocks and slides along them. |

## Manoeuvring thrusters (`ThrusterSpec`)

| Field | Units | What it does |
|---|---|---|
| `ReverseThrust` | N | Braking and flying astern. |
| `LateralThrust` | N | Sideways. Also what yaw bends the path with in a safe turn. |
| `VerticalThrust` | N | Up and down. Also what pitch bends the path with: **the main number for safe turns**, since a ship turns mostly by pitching. |
| `Torque` | N m | About X (pitch), Y (yaw), Z (roll). How fast the nose swings (acceleration) and, with the hull's `VeilSpin`, its top turn rate. |

More thrust with the same hull means more acceleration that way and a higher top speed that way. To get more acceleration (quicker out of a slide, tighter turns) without the ship strafing faster, raise the hull's `VeilCross` as well.

## Main engine (`EngineSpec`)

The engine has its own controller, a small computer with firmware in ROM (`software/engine/engine.asm`), separate from the ship's computer. The ship asks for thrust; the controller gives it until it reckons the core too hot, then cuts the engine out and lets it pulse. It does not measure the core: it works its temperature out from the coolant's, as the cooling unit reports it, plus 80 C for each rating's worth of thrust, and only looks four times a second. Cut-out 250 C, resume 200 C (in the firmware).

| Field | Units | What it does |
|---|---|---|
| `RatedThrust` | N | What the controller gives at full, as it leaves the factory. With the hull's mass and `VeilDrag` it sets the top speed. |
| `MaxThrust` | N | What the engine can physically give, whatever its controller is told (a hacked trim): unpublished. |
| `HeatAtRated` | MW | Heat into the core at the rating; it grows with the thrust. |
| `CoreCapacity` | MJ/K | How much heat the core soaks before it warms: higher takes longer to reach the cut-out. |
| `CoreConductance` | MW/K | Core to coolant. |
| `CoolantCapacity` | MJ/K | The coolant loop's heat capacity. |
| `RadiatorConductance` | MW/K | Coolant to space, with an even share of power to the engines. Higher: more thrust kept up without cutting out. |
| `SinkTemperature` | C | What the radiators bring the coolant down to with nothing to carry. |
| `CoolingWithNoPips`, `CoolingWithMostPips` | factor | How much harder the cooling works with no power to the engines and with the most (an even share is 1). |
| `BoostCooling` | factor | The cooling boost (the pilot's boost key, held): pumps and radiators driven this many times harder. |
| `BoostMegawatts` | MW | What the boost draws from the engines capacitor. |
| `SafeTemperature` | C | Above it the core wears: the engine loses thrust for good (until repaired by the insurer). |
| `WearPerDegreeSecond` | share | Thrust lost for each degree over safe, each second. |
| `FailTemperature` | C | The engine fails here. |
| `FailDamage` | points | What the failure does to the hull, past the shields (the default destroys a fighter). |
| `RadiatorGlowPerMegawatt` | heat units/MW | What heat seekers see of the radiators for each MW they give off: cooling hard is a bright target. |
| `CoreGlow` | heat units | What heat seekers see of a core at the failing temperature. |
| `FirmwarePath`, `InstructionsPerSecond` | | The controller's firmware and speed. |

At steady thrust f (of the rating) the core settles at about sink + f x heat x (1 / (radiators x cooling) + 1 / core conductance). With the D-50 on an even power share: full throttle gives the rating for about 24 s, then pulses at about 56%; half throttle never cuts out; with the boost held it keeps up about 85%. More power to the engines keeps up more.

The controller's flaws, for players who write their own software: it trusts the coolant reading (the cooling unit has a calibration mode), it only looks at the core four times a second, and it has a bench mode, opened by a service sequence, with no cut-out and a thrust trim. All of them get more thrust out of it, and all of them cost the engine: it wears, it can fail, and past the hull's rating the structure breaks. The registers are in `scripts/Ship/EngineDevice.cs`.

## Jammer (`JammerSpec`)

| Field | Units | What it does |
|---|---|---|
| `NoiseAt1Km` | ratio | The noise it puts into a radar 1 km away, sending all round. Falls with the square of the distance, the echo with the fourth power, so radars burn through close in. |
| `BeamGain` | factor | Aimed (RWR.COM aims it at the radar that locks the ship), how much more it puts into its beam. 1: it cannot aim. |
| `BeamDegrees` | degrees | The aimed beam's width. |
| `Deception` | bool | It can copy a tracking radar's echo and walk the copy away (range gate pull-off). Without it, asking for deception gives noise. |
| `RepeaterAt1Km` | ratio | The copy's strength at 1 km: it must beat the skin echo to take the tracker. |
| `CaptureSeconds` | s | The copy sits on the echo this long first. |
| `PullAcceleration` | m/s² | How fast the copy runs away. Faster than the victim's tracker can follow and the tracker drops it (about 100 m/s² against the fighter's radar). |
| `MaxPull` | m | How far it pulls the gate before it stops. |
| `DropSeconds` | s | Silent after a pull, so the gate is left on nothing. |
| `Megawatts` | MW | Drawn from the systems capacitor (against the shields); all of it ends as heat. |
| `Signature`, `HeardAt1Km`, `ToneHz` | | What warning receivers make of it: they hear a jammer far beyond the range it hides the ship at. |

## The fighter as it is

| | Value |
|---|---|
| Hull | 20 t; inertia 80 000 / 100 000 / 40 000 kg m²; veil 3.1e-4 along, 0.006 across; spin 0.93 / 1.75 / 0.47 |
| Thrusters (T-35) | reverse 0.7 MN, sideways 1.0 MN, up and down 2.0 MN; torque 168 000 / 112 000 / 168 000 N m |
| Engine (D-50) | rated 1.0 MN (50 m/s²), most 1.6 MN |
| Top speeds | about 400 m/s ahead, 336 astern, 91 sideways, 129 up and down |
| Top turn rates | pitch 1.5 rad/s (86 deg/s), yaw 0.8 (46), roll 3.0 (171) |
| Safe turns (B) | about 52 deg/s at 150 m/s, 42 at 250, 40 at 380, keeping most of the speed |
| Free turns | the nose swings at 86 deg/s, the path at 51-58 deg/s, sliding 40-60 degrees and losing half to three quarters of the speed in 3 s |

## Recipes

- **Faster ship:** more `RatedThrust` on the engine, or less `VeilDrag` on the hull (it also coasts further).
- **Tighter safe turns:** more `VerticalThrust` (and `LateralThrust` for yaw) on the thrusters, more `VeilCross` on the hull (most effect at high speed), or a lighter hull.
- **Quicker nose (aiming):** more `Torque`, less `Inertia`, or less `VeilSpin` (a higher top rate).
- **Heavy, sluggish ship:** more `Mass` and `Inertia`, low `VeilCross`: it slides through its turns.
- **Engine that runs hot:** more `HeatAtRated` or less `RadiatorConductance`: it cuts out sooner and pulses at a lower share.
- **Cheap jammer:** noise only, `BeamGain` 1; a better one aims; the best also deceives.

The tests that measure all this: `drive_test` (veil, engine), `flight_test` (FLIGHT against the rating plate), `jammer_test`.
