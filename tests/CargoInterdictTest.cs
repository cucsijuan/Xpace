using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Xpace.Ship;
using Xpace.Space;
using Xpace.World;

namespace Xpace.Tests;

/// <summary>
/// A cargo ship on its record's way is a ship like any other:
///   - in cruise its wake shows on radar;
///   - its crew tunes the generator against an interdictor's beam, a moment behind it, as a pilot does;
///   - a beam that gets through slows it, and past what its generator bears it drops out, resting or not;
///   - the time it loses comes off its record: it arrives later;
///   - with the beam gone it spools up and goes on;
///   - a fixed interdictor works on it by the same rules out of sight, and a quicker crew loses less time.
///
/// Run: Godot --headless --fixed-fps 60 --path . res://tests/cargo_interdict_test.tscn
/// Exits with 0 if every check passed.
/// </summary>
public partial class CargoInterdictTest : Node
{
    private int _failures;

    public override async void _Ready()
    {
        Fixed();
        await Beam();
        GD.Print(_failures == 0 ? "ALL PASSED" : $"{_failures} FAILED");
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GetTree().Quit(_failures == 0 ? 0 : 1);
    }

    private void Fixed()
    {
        StationBook book = StationBook.Read(GD.Load<SectorMap>("res://assets/sectors/map.tres"));
        ShipModel model = GD.Load<ShipModel>("res://assets/ship/models/freighter.tres");
        Voyage.Space space = book.Space["sector_01"];
        Check("the book knows the fixed interdictor", space.Beams.Count == 1);
        double lag = model.CrewTuneSeconds;
        double quick = Flight(book, model, 0.2), slow = Flight(book, model, 8);
        model.CrewTuneSeconds = lag;
        var beams = space.Beams.ToList();
        space.Beams.Clear();
        double clear = Flight(book, model, lag);
        space.Beams.AddRange(beams);
        Check($"out of sight it costs time ({clear:0} s clear, {slow:0} s with a slow crew)", slow > clear + 10);
        Check($"a quicker crew loses less ({quick:0} s against {slow:0} s)", quick < slow);
    }

    // A record flying from the first station to the yard, out of sight, with no scene: how long it takes
    private static double Flight(StationBook book, ShipModel model, double lag)
    {
        model.CrewTuneSeconds = lag;
        var state = new UniverseState { Clock = 1 };
        var owners = new Dictionary<string, OrganisationRecord>();
        IAccount Account(string id) => CargoShipsTest.Account(state, owners, id);
        CargoShips.Muster(state, book, Account);
        CargoShips.Muster(state, book, Account);
        CargoShipRecord ship = state.CargoShips.Values.First(s => s.Home == "ALPHA");
        foreach (CargoShipRecord other in state.CargoShips.Values.Where(s => s != ship))
            other.NextLook = 1e12;
        StationBook.Station from = book.Find("ALPHA"), to = book.Find("DRYDOCK");
        double total = book.Departure(from, null, to, model, 0, out Vector3 contact).Sum(l => l.Seconds + l.Transit);
        ship.Phase = CargoShipRecord.Flying;
        ship.State = CargoShipRecord.Fetching;
        ship.From = from.Id;
        ship.Going = to.Id;
        ship.LeftDock = ship.LeftPad = ship.Dock = ship.Pad = 0;
        ship.ContactPoint = contact;
        ship.Left = ship.WorkedTo = state.Clock;
        ship.Arrives = ship.Left + total;
        double start = state.Clock;
        while (ship.Phase == CargoShipRecord.Flying && state.Clock < start + 7200)
        {
            state.Clock += 5;
            CargoShips.Tick(state, book, Account);
        }
        return state.Clock - start;
    }

    private async Task Beam()
    {
        Universe.Current.Forget();
        var main = GD.Load<PackedScene>("res://scenes/main.tscn").Instantiate<SectorHost>();
        AddChild(main);
        foreach (string name in new[] { "Drone1", "Drone2", "Raider", "InterdictorBuoy" })
            main.Sector.GetNodeOrNull(name)?.QueueFree();
        CargoView view = main.GetNode<CargoView>("CargoView");
        await Seconds(3);
        UniverseState state = Universe.Current.State;
        StationBook book = Universe.Current.Book;
        ShipModel model = GD.Load<ShipModel>("res://assets/ship/models/freighter.tres");
        book.Space["sector_01"].Beams.Clear();      // this test's beam only

        // One on the long way to the other sector, far from any mass
        CargoShipRecord record = state.CargoShips.Values.First(s => s.Home == "ALPHA");
        foreach (CargoShipRecord other in state.CargoShips.Values.Where(s => s != record))
            other.NextLook = 1e12;
        StationBook.Station from = book.Find(record.At), to = book.Find("DRYDOCK");
        var legs = book.Departure(from, null, to, model, 0, out Vector3 contact);
        double total = legs.Sum(l => l.Seconds + l.Transit);
        record.Phase = CargoShipRecord.Flying;
        record.State = CargoShipRecord.Fetching;
        record.From = from.Id;
        record.Going = to.Id;
        record.LeftDock = record.LeftPad = 0;
        record.Dock = record.Pad = 0;
        record.ContactPoint = contact;
        record.Left = state.Clock - legs[0].Seconds * 0.45;
        record.Arrives = record.Left + total;
        await Seconds(3);
        Check("it is shown", view.Shown.TryGetValue(record.Id, out ShipBody ship));
        if (ship == null)
            return;
        ship.Rest();
        await Seconds(1);
        float cruising = ship.LinearVelocity.Length();
        ShipSignature signature = ship.Find<ShipSignature>();
        var cruise = ship.Find<CruiseDevice>();
        Check($"it cruises ({cruising:0} m/s) with its wake on radar ({signature.RadarCrossSection:0} m2)", cruising > 500 && signature.RadarCrossSection > 200);

        // A beam that stays on one frequency: once the crew has tuned to it, it hardly counts
        double arrives = record.Arrives;
        await Held(cruise, 100, model.CrewTuneSeconds + 8, wanders: false);
        Check($"against a steady beam the crew tunes and holds on ({ship.LinearVelocity.Length():0} m/s)", record.Generator.On && ship.LinearVelocity.Length() > cruising * 0.7f);

        // A light beam that wanders: slower, still in cruise
        await Held(cruise, 5, 2);
        float slowed = ship.LinearVelocity.Length();
        Check($"a weak beam slows it ({slowed:0} m/s), still in cruise", slowed < cruising * 0.8f && record.Generator.On);

        // A strong one that wanders faster than the crew follows: the generator gives up
        double held = await HeldUntil(cruise, 100, () => !record.Generator.On, 90);
        GD.Print($"it held on for {held:0.0} s");
        await Held(cruise, 100, 2);
        float normal = model.Hull.TopSpeed(model.Engine.RatedThrust);
        Check($"a strong one takes it out of cruise, resting or not ({ship.LinearVelocity.Length():0} m/s, engine's top {normal:0})", !record.Generator.On && ship.Dormant && ship.LinearVelocity.Length() <= normal * 1.05f);
        Check($"no wake on radar now ({signature.RadarCrossSection:0} m2)", signature.RadarCrossSection < 100);
        Check($"it will get there later ({record.Arrives - arrives:0} s)", record.Arrives > arrives + 3);

        // Beam gone
        await Seconds(1);
        Check("it doesn't start while the generator spools", !record.Generator.On);
        await Seconds(model.Generator.SpoolSeconds + 8);
        Check($"it starts again and goes on ({ship.LinearVelocity.Length():0} m/s)", record.Generator.On && ship.LinearVelocity.Length() > cruising * 0.7f);
    }

    private double _beamTime;
    private readonly InterdictorSpec _spec = new();

    private void Beam(CruiseDevice cruise, float density, bool wanders)
    {
        _beamTime += 1.0 / Engine.PhysicsTicksPerSecond;
        cruise.Thicken(density, wanders ? _spec.Band(_beamTime) : (byte)200);
    }

    private async Task Held(CruiseDevice cruise, float density, double seconds, bool wanders = true)
    {
        for (int i = 0; i < seconds * Engine.PhysicsTicksPerSecond; i++)
        {
            Beam(cruise, density, wanders);
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        }
    }

    private async Task<double> HeldUntil(CruiseDevice cruise, float density, Func<bool> done, double seconds)
    {
        int i = 0;
        for (; i < seconds * Engine.PhysicsTicksPerSecond && !done(); i++)
        {
            Beam(cruise, density, true);
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        }
        return (double)i / Engine.PhysicsTicksPerSecond;
    }

    private async Task Seconds(double seconds)
    {
        for (int i = 0; i < (int)Math.Round(seconds * Engine.PhysicsTicksPerSecond); i++)
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
    }

    private void Check(string what, bool ok)
    {
        GD.Print($"{(ok ? "ok  " : "FAIL")} {what}");
        if (!ok)
            _failures++;
    }
}
