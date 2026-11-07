# Performance

How the game stays inside its time, what is there to use, and what every new feature has to respect. Read this before building anything that runs every step or looks at many things.

## The budget

The physics runs at 60 steps a second: a step has **16.67 ms**, everything included. A step that takes longer slows the game down, so what counts is the step's mean *and* its 95th percentile.

What fills it is the number of ships. Every ship is a real computer, a real radar and a real warning receiver, so the question for any feature is what it costs **per ship**, and whether that grows as ships are added. The cost of a feature must grow with the ships *near* something, never with every ship times every ship.

Measured in `swarm_test` (headless, 16 cores, real AI fighters in a fight, 2026-11-05): 50 ships 8.0 ms a step, 100 ships 17.7 ms (95th percentile 24 ms).

## How a step runs

A ship's step has three stages, and all the ships go through each together (`ShipBody.StepAll`):

1. **Get ready**, one ship at a time: controls, sensors and registers on the bus, every device's `BeforeStep`, the interrupt raised.
2. **Compute**, all at once: every ship's emulated computer runs on the processor's cores (`Parallel.For`). A machine touches nothing outside itself: no call goes from the C code into C#, and the native libraries keep no writable state of their own.
3. **Act**, one ship at a time: every device's `AfterStep`, the engine, power, thrust and motion.

The first ship the scene tree reaches runs the stages for all of them. A ship that stops processing (a puppet, a paused game) is left out.

What follows from it:

- Nothing in stage 2 may touch Godot or anything shared. Keep `ShipComputer.Advance` a plain call into the machine; a new call back into C# from the native side would break it.
- In `BeforeStep` no other ship has moved yet in this step; in `AfterStep` some have. A device must not depend on the order of the ships.
- Node scripts (pilots, missiles, rounds) run after the ships, in tree order, as usual.

`tests/parallel_computers_test` checks that machines run side by side end byte for byte as they do in turn.

## What is there to use

| Need | Use | Instead of |
|---|---|---|
| Who is near a point | `Spatial.Grid(from, layer)` (`Within`, `Reaching`, `Nearest`) | walking a group and measuring every distance |
| A kind of thing asked about by distance | a new `Spatial.Layer`: group, cell size, where it stands | a list of your own |
| Members of a group in this sector | `Sectors.InGroup(from, group)`, kept for the step | `GetTree().GetNodesInGroup` |
| Something worked out once a step from a group, shared by all who ask | `Sectors.Built(from, group, key, make)` | each ship working it out again |
| What radars see | `RadarPicture.Of(node)` (`All`, `Near`, `Detectable`) | asking every target for its place |
| What warning receivers hear | `EmitterPicture.Of(node)`; emitters take down how they stand once in `Stand()` | asking every emitter |
| Jammers' noise | `JammerPicture`, `Jamming.Lines` once, then `Jamming.Noise(lines, ...)` per echo | adding up the jammers for each echo |
| Knowing a step has gone by | `Sectors.Step` | `Engine.GetPhysicsFrames()` |
| Noise for sensors | `GaussianNoise` | `RandomNumberGenerator`, one engine call a draw |
| Drawing only what someone sees | `ShipComputer.Watch()` / `Unwatch()` | drawing always |
| Working only when a program asks | `ShipBus.TakeRead(slot, register)`, as the radio does | filling registers nobody reads |

Pictures and grids are made at the first question of a step, from where things stood then, and thrown away. Use them to pick who is near and test the exact distance at the time of use. Anything in them may have been freed since: check `IsInstanceValid` before calling into a node. A list a picture hands out is reused by the next question: go through it, do not keep it.

## Rules

**No scanning everyone.** A loop over all the ships inside something every ship does is n x n. If there is no layer for what you look for, add one. A loop over everything is fine once a step in a picture, or for what happens rarely.

**Calls into Godot are the expensive part.** Pure C# arithmetic costs almost nothing beside them. In anything that runs per ship per step, and above all per pair of things:

- read `GlobalPosition`, `GlobalBasis`, `GetWorld3D()` once and keep them in a local or pass them down;
- no `GetChildren`, `FindChildren`, `GetNode`, `IsVisibleInTree`, `GetParent` walks: find things once in `_Ready`;
- no `Godot.Collections.Dictionary` or `Array` lookups: copy an exported one into a C# collection once;
- no `Resource.ResourcePath` and nothing built on it in a loop;
- no `RandomNumberGenerator`, no `Engine.*` counters.

**Cheap test first.** Order the conditions so most candidates leave at the cheapest one: range, then angle, and only then rocks, line of sight or anything that walks a structure.

**Look things up by key.** A `List.Find` inside a loop over contacts is n x n again, and allocates. Keep a dictionary beside the list.

**Do it once for all.** What is the same for every receiver (where an emitter stands) or every echo (what the jammers add) is worked out once, by whoever owns it.

**Do it only when it matters.** Slower than every step when it can be (a radar sweeps, it does not look every step), and not at all when nobody reads or sees the result. A result nobody can tell apart from the full work is the only kind of saving allowed in the simulation.

**Never a cheaper second version.** Speed never comes from a simplified copy of a rule for ships that matter less. One system, everyone uses it; a ship seen and unseen behave the same, and a player's software runs in every ship. Look for sharing, parallel work or work left undone first.

**Designers keep their data.** Optimising does not move numbers out of specs and scenes into code.

**Nothing long in one step.** A step has no room for a job that takes tens of milliseconds, however rarely it runs. Work that can wait goes a piece at a time: an iterator that yields, run by `Universe.Slice` in `SliceMilliseconds` of each step (`CargoShips.Work`, `UniverseStore.Documents`), with nothing but its own list to go through, as records can go while it runs. What touches no node can go to another thread: writing files, reading a scene (`SectorHost.Prepare`).

**Allocate little.** The collector stops everything. Left to itself it waits for tens of megabytes and takes tens of milliseconds, so `Universe.PaceCollector` collects the young generation every `CollectEveryMegabytes`, a few milliseconds each. (The runtime's own setting for it, `DOTNET_GCgen0size`, works only as an environment variable: Godot ignores it in the runtime config.) What is allocated every step decides how often that happens.

**Sessions.** Everything here has to hold on a host running several sectors: grids and pictures are per sector, and a puppet has no computer to run. Test the feature in a session as usual.

## Measuring

Measure before optimising and after, on the whole step.

```sh
XPACE_SWARM=50 XPACE_SWARM_SECONDS=40 godot --headless --fixed-fps 60 --path . res://tests/swarm_test.tscn
```

`XPACE_SWARM` is ships per side. It prints the whole step (mean, median, 95th percentile, worst), then each kind of device on its own, the computers one after the other and side by side.

To know where the time really goes, put stopwatches inside the step: `python3 tools/perf/step_costs.py`, build, run the swarm test, and it prints `COST` lines per node script and device. Then take them out (the script's header says how). They are never committed.

Things that have misled us:

- **Stack samplers** (`dotnet-stack`) stop the thread where it can be stopped, which is mostly at calls into Godot. They point at things worth a look but their percentages are not times: a fix they promised 6 ms for gave nothing.
- **A device timed alone** runs with warm caches and shows less than it costs in a battle. Trust the stopwatches inside the real step.
- **One run** of the same battle varies by half a millisecond or more. A gain smaller than that is not a gain yet.
- **Headless** leaves rendering out. Check big changes in a window too.

The mean hides hitches: a game can average 4 ms and stutter. Look at the worst steps too, and for pauses compare with `GC.GetTotalPauseDuration()`.

Look at how a cost grows, not only at its size: time it with 25 and 50 per side. Twice the ships for twice the time is fine; four times means something is n x n.

## Before calling a feature done

- [ ] Does it run every step? What does it cost per ship, and does that grow with the number of ships?
- [ ] Does it look at other things? Through a `Spatial` layer or a picture, not a walk over a group?
- [ ] Does anything inside a per-ship or per-pair loop call into Godot?
- [ ] Is anything worked out once per ship that could be worked out once for all?
- [ ] Does it run when nobody reads, sees or hears the result?
- [ ] Can any single call take more than a couple of milliseconds? Then in pieces, or on another thread.
- [ ] Does it hold in the three stages: nothing in the compute stage, no dependence on the order of the ships?
- [ ] If it can matter in a battle: `swarm_test` before and after, whole step.

## Positions and the moving origin

The world's origin moves under everything as a ship flies (`FloatingOrigin`, every kilometre). A world position in a `Vector3` is only good until the end of the step it was worked out in.

- **Anything that keeps a world position past the step keeps a `Spot`**: `Spot.At(node, world)` to keep it, `.World` to use it, `.Moved(by)` to move it, `Spots` for a queue of them. No one has to be told of a shift.
- Between things that exist now, use their positions as they are: `a.GlobalPosition.DistanceTo(b.GlobalPosition)` is always right.
- Directions, speeds and what is relative to a node are plain `Vector3`s.
- `IOriginShift` is only for what is too many or is drawn every frame (rounds, trails, dust).
- `tests/places_test` goes through every field in the game: a new position that is not a `Spot` fails it until someone decides, with a reason on its lists.
