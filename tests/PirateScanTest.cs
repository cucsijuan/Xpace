using System.Collections.Generic;
using System;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Xpace.Ship;
using Xpace.Space;
using Xpace.World;

namespace Xpace.Tests;

/// <summary>
/// A pirate doesn't know what a ship carries until its scanner has read it. Runs with real ships where no player
/// is: an empty freighter is followed, read and let go, and remembered; a loaded one is read and then fired on. No shot
/// is fired before the reading ends. Then one met in cruise, on a long way: the pirate leaves its own way going as it
/// went, catches up in cruise, and comes alongside to read it without ever running into it or pulling it out of cruise.
///
/// Run: Godot --headless --fixed-fps 60 --path . res://tests/pirate_scan_test.tscn
/// </summary>
public partial class PirateScanTest : Node
{
    private int _failures;
    private SectorHost _main;
    private CargoView _view;

    public override async void _Ready()
    {
        Universe.Current.Forget();
        Universe.Current.Population = GD.Load<PopulationSpec>("res://assets/people/population.tres");
        _main = GD.Load<PackedScene>("res://scenes/main.tscn").Instantiate<SectorHost>();
        AddChild(_main);
        foreach (string name in new[] { "Drone1", "Drone2", "Raider", "InterdictorBuoy" })
            _main.Sector.GetNodeOrNull(name)?.QueueFree();
        _view = _main.GetNode<CargoView>("CargoView");
        _view.MeetEverySeconds = 1;
        _main.FightQuietSeconds = 5;
        await Seconds(3);
        UniverseState state = Universe.Current.State;
        foreach (CargoShipRecord other in state.CargoShips.Values)
            other.NextLook = 1e12;
        PersonRecord pirate = state.People.Values.First(p => p.Livelihood == "Pirate");
        foreach (PersonRecord other in state.People.Values.Where(p => p != pirate))
            other.Memories.Clear();
        CargoShipRecord attacker = state.CargoShips.Values.First(s => s.Owner == pirate.Id);
        ShipModel freighter = GD.Load<ShipModel>("res://assets/ship/models/freighter.tres");
        var victims = state.CargoShips.Values.Where(s => s.Model == freighter.ResourcePath).Take(3).ToList();

        // Empty
        await Meeting(attacker, victims[0], 0, "");
        Check($"the pirate goes after the empty ship ({attacker.Phase})", await Until(() => attacker.Phase == CargoShipRecord.Loose, 30));
        ShipBody raider = _view.Shown.GetValueOrDefault(attacker.Id);
        var scanner = raider?.Device<ScannerDevice>();
        var ai = raider?.Find<FighterAi>();
        Check("it has a scanner", scanner != null);
        Check("with someone to look for, its radar comes on", await Until(() => raider.Device<RadarDevice>().On, 3));
        bool fired = false;
        void Fired(Node3D by, Vector3 at) => fired |= by == raider;
        Combat.Fired += Fired;
        double read = await Watch(attacker, raider, () => scanner.Result != null || attacker.Phase != CargoShipRecord.Loose, 180);
        Check($"it reads the hold with its radar on it ({read:0} s): nothing aboard", scanner.Result?.Tonnes == 0);
        Check("it never fired", !fired);
        Check("the ship it read never turned its radar on", _view.Shown.GetValueOrDefault(victims[0].Id)?.Device<RadarDevice>()?.On != true);
        Check("and goes back on its way", await Until(() => attacker.Phase == CargoShipRecord.Flying, 60));
        Check("its radar off again once it has nobody to look for", await Until(() => !raider.Device<RadarDevice>().On, 15));
        Check("remembering there was nothing there", Memories.Knows(pirate, victims[0].Id, Memories.NothingAboard, state.Clock, 3600));
        Check("that ship is left alone from now on", !People.Meetings(state, Universe.Current.Book, 1e9f).Any(m => m.Attacker == attacker && m.Victim == victims[0]));

        // Loaded
        await Meeting(attacker, victims[1], 10, "iron");
        Check("the pirate goes after the next", await Until(() => attacker.Phase == CargoShipRecord.Loose, 30));
        raider = _view.Shown.GetValueOrDefault(attacker.Id);
        scanner = raider?.Device<ScannerDevice>();
        fired = false;
        read = await Watch(attacker, raider, () => scanner.Result != null, 180);
        Check($"it reads what it carries ({read:0} s): {scanner.Result?.Tonnes} t", scanner.Result?.Tonnes == 10);
        Check("not one shot before that", !fired);
        Check("then it opens fire", await Until(() => fired, 60));
        Combat.Fired -= Fired;

        // In cruise, a gate's way off: both on it, the freighter well ahead
        Check("a third freighter", victims.Count == 3);
        _view.Shown.GetValueOrDefault(attacker.Id)?.QueueFree();      // the last fight's ship goes: its record is put on a new way
        await Meeting(attacker, victims[2], 10, "iron", "ALPHA", 0.2);
        Check("the pirate goes after one in cruise", await Until(() => attacker.Phase == CargoShipRecord.Loose, 30));
        raider = _view.Shown.GetValueOrDefault(attacker.Id);
        ShipBody prey = _view.Shown.GetValueOrDefault(victims[2].Id);
        scanner = raider?.Device<ScannerDevice>();
        Check($"it leaves its way going as it went ({raider?.LinearVelocity.Length():0} m/s), its generator on", raider?.LinearVelocity.Length() > 300 && raider.Device<CruiseDevice>().On);
        int crashes = 0;
        void Crashed(float joules) => crashes++;
        raider.Impact += Crashed;
        prey.Impact += Crashed;
        var interdictor = raider.Device<InterdictorDevice>();
        bool pulled = false;
        read = await Watch(attacker, raider, () =>
        {
            pulled |= interdictor.On;
            return scanner.ResultOf == prey && scanner.Result != null;
        }, 150);
        Check($"it catches up and reads it ({read:0} s): {scanner.Result?.Tonnes} t", scanner.ResultOf == prey && scanner.Result?.Tonnes == 10);
        Check("without pulling it out of cruise only to look it over", !pulled);
        Check($"never running into it ({crashes} knocks)", crashes == 0);
        if (IsInstanceValid(raider))
            raider.Impact -= Crashed;
        if (IsInstanceValid(prey))
            prey.Impact -= Crashed;

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GD.Print(_failures == 0 ? "ALL PASSED" : $"{_failures} FAILED");
        GetTree().Quit(_failures == 0 ? 0 : 1);
    }

    // Both on the same way, in the sector no player is in
    private async Task Meeting(CargoShipRecord attacker, CargoShipRecord victim, int tonnes, string good, string going = "DRYDOCK", double along = 0.5)
    {
        UniverseState state = Universe.Current.State;
        StationBook book = Universe.Current.Book;
        StationBook.Station from = book.Find("KESTREL"), to = book.Find(going);
        foreach (CargoShipRecord s in new[] { victim, attacker })
        {
            ShipModel m = GD.Load<ShipModel>(s.Model);
            var way = book.Departure(from, null, to, m, 0, out Vector3 contact);
            s.Phase = CargoShipRecord.Flying;
            s.At = from.Id;
            s.From = from.Id;
            s.Going = to.Id;
            s.LeftDock = s.LeftPad = s.Dock = s.Pad = 0;
            s.ContactPoint = contact;
            s.Left = state.Clock - way.Sum(l => l.Seconds) * along;
            s.Arrives = s.Left + way.Sum(l => l.Seconds + l.Transit);
            s.WorkedTo = state.Clock;
        }
        victim.Tonnes = tonnes;
        victim.Good = good;
        await Seconds(0.1);
    }

    private async Task<double> Watch(CargoShipRecord attacker, ShipBody raider, Func<bool> done, double seconds)
    {
        double from = Universe.Current.State.Clock;
        var ai = raider?.Find<FighterAi>();
        var scanner = raider?.Device<ScannerDevice>();
        var radar = raider?.Device<RadarDevice>();
        for (int i = 0; i < seconds * 60 && raider != null && !done(); i++)
        {
            if (i % 240 == 0 && IsInstanceValid(raider))
            {
                Node3D target = ((EscortDuty)ai.Engagement).Inspecting ?? ((EscortDuty)ai.Engagement).Attackers.FirstOrDefault();
                JammerDevice jammer = (target as ShipBody)?.Device<JammerDevice>();
                GD.Print($"     {i / 60,3} s  {ai.Doing,-18} speed {raider.LinearVelocity.Length(),6:0}  prey {(target as ShipBody)?.LinearVelocity.Length(),6:0} at {target?.GlobalPosition.DistanceTo(raider.GlobalPosition),8:0} m  lock {radar.Locked != null,-5} jam {jammer?.Mode}  held {raider.Find<FighterAi>().Target != null} scan {scanner.Progress:0.00}");
            }
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        }
        return Universe.Current.State.Clock - from;
    }

    private async Task Seconds(double s)
    {
        for (int i = 0; i < s * 60; i++)
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
    }

    private async Task<bool> Until(Func<bool> done, double seconds)
    {
        for (int i = 0; i < seconds * 60; i++)
        {
            if (done())
                return true;
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        }
        return done();
    }

    private void Check(string what, bool ok)
    {
        GD.Print($"{(ok ? "ok  " : "FAIL")} {what}");
        if (!ok)
            _failures++;
    }
}
