using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Xpace.Computer;
using Xpace.Ship;
using Xpace.Space;
using Xpace.World;

namespace Xpace.Tests;

/// <summary>
/// Getting into a station's hangar unseen (ships here are hulls with a radio; their computers stay off):
///
///   - a ship with its radio on, out in front of a hangar: the hangar's watch knows it, nothing happens;
///   - one with its radio off: noticed (radar and heat), the hangar shuts and everyone in the zone is told; going in
///     all the same, it is hostile; the hangar opens again once it is gone;
///   - right behind a ship the station knows, in its radar cell and its heat: not told apart, not noticed;
///   - a cold ship out of radar range is still seen by its heat when near; outside the watch's cone (along the hull to
///     the mouth) it is not seen at all;
///   - in unseen, on a free pad: the clamps take it, the station counts it docked (it may trade), and nobody's pad is
///     given away under it; pushing up, the clamps let it go; a pad a cargo ship has is kept for it.
///
/// Run: Godot --headless --fixed-fps 60 --path . res://tests/stealth_docking_test.tscn
/// Exits with 0 if every check passed.
/// </summary>
public partial class StealthDockingTest : Node3D
{
    private StationRadio _station;
    private StationSecurity _security;
    private HangarDock _dock;
    private HangarWatch _watch;
    private int _failures;

    public override async void _Ready()
    {
        Universe.Current.Forget();
        AddChild(new Projectiles());
        var station = GD.Load<PackedScene>("res://scenes/stations/synty_station.tscn").Instantiate<Node3D>();
        station.Name = "Haven";
        AddChild(station);
        _station = station.GetNode<StationRadio>("Radio");
        _security = station.GetNode<StationSecurity>("Security");
        _security.Scans = false;                        // cargo scans are police_scan_test's
        _security.PoliceCount = 0;
        _dock = station.GetNode("HangarBig1").GetNode<HangarDock>("Dock");
        _watch = _dock.Watch;
        Check("the hangar has its watch, an approach radar heard as A", _watch != null && _watch.Radar?.Signature == "A");
        await Seconds(0.5);

        // Known: its radio on
        ShipBody known = Ship("Kilo", Out(250));
        await Seconds(3);
        Check("a ship with its radio on is known: not noticed", _watch.NoticedAgo(known) == null && !_watch.Alarm);

        // Unknown, in plain sight
        ShipBody bystander = Ship("Lima", _station.GlobalPosition + new Vector3(0, 3000, 0));
        ShipBody sneak = Ship("Mike", Out(300) + _dock.GlobalBasis.X * 40, radio: false);
        Check("a ship with its radio off out in front: noticed", await Until(() => _watch.NoticedAgo(sneak) != null, 4));
        Check("the hangar shuts", await Until(() => _station.LockedDown(_dock) && !_dock.IsOpen, 1));
        Check($"and the zone is told ({Last(bystander)})", Last(bystander).Contains($"Unidentified ship at hangar {_dock.Number}"));
        sneak.GlobalPosition = _dock.InsidePoint;
        Check("going in all the same: hostile", await Until(() => _security.StandingOf(sneak) == Standing.Hostile, 2));
        sneak.QueueFree();
        Check($"gone: the hangar opens again ({Last(bystander)})", await Until(() => !_station.LockedDown(_dock), 20) && Last(bystander).Contains("open again"));
        known.QueueFree();
        await Seconds(1);

        // In a known ship's shadow
        Vector3 inward = -_dock.GlobalBasis.Z.Normalized();
        ShipBody leader = Ship("November", Out(400));
        ShipBody shadow = Ship("Oscar", Out(412), radio: false);
        leader.LinearVelocity = shadow.LinearVelocity = inward * 10;
        await Seconds(4);
        Check("right behind a known ship: its echo and its heat are that ship's", _watch.NoticedAgo(shadow) == null);
        leader.QueueFree();
        shadow.QueueFree();
        await Seconds(1);

        // Cold, and seen only near; along the hull, not at all
        ShipBody cold = Ship("Papa", Out(2900), radio: false);
        cold.GetNode<ShipSignature>("Signature").RadarCrossSection = 0.5f;     // small: past its radar range
        await Seconds(4);
        bool farUnseen = _watch.NoticedAgo(cold) == null;
        cold.GlobalPosition = Out(150);
        bool nearSeen = await Until(() => _watch.NoticedAgo(cold) != null, 3);
        Check($"a small cold ship far out is not seen ({farUnseen}), near it is, by its heat ({nearSeen})", farUnseen && nearSeen);
        cold.QueueFree();
        await Until(() => !_watch.Alarm, 20);

        ShipBody hugger = Ship("Quebec", _dock.GlobalTransform * new Vector3(60, -4, 6), radio: false);
        await Seconds(4);
        Check("along the hull to the mouth, out of the cone: not seen", _watch.NoticedAgo(hugger) == null && !_watch.Alarm);
        Radio(Ship("Romeo", Out(600))).AskDocking();
        Check("a known ship is cleared in: the door opens", await Until(() => _dock.IsOpen && _dock.Door.At >= _dock.PassableAt, 30));
        LandingPad pad = _dock.Pads.First(p => p.AssignedTo == null && _station.PilotOn(_dock.Number, p.Number) == false);
        hugger.GlobalTransform = pad.GlobalTransform * pad.Rest(hugger).Translated(new Vector3(0, 2, 0));
        hugger.LinearVelocity = Vector3.Zero;
        Check("in unseen, sitting on a free pad: the clamps take it", await Until(() => pad.HeldShip == hugger && pad.Stowaway, 3));
        Check("not hostile, nobody knows", _security.StandingOf(hugger) == Standing.Clean && _watch.NoticedAgo(hugger) == null);
        Check("docked, as the market sees it", await Until(() => _station.DockedHere(hugger), 10));
        Check("its pad is nobody's to give, a cargo ship's no more than a pilot's", pad.Occupant == hugger && _station.Occupied(_dock.Number, pad.Number));
        _up = 1;
        Check("pushing up, the clamps let it go", await Until(() => pad.HeldShip == null && hugger.Holder == null, 10));
        _up = 0;

        // A pad a cargo ship of the universe's has: the clamps keep it for that ship
        HangarDock other = _station.Docks.First(d => d != _dock && d.Pads.Count > 0);
        LandingPad kept = other.Pads[0];
        Universe.Current.State.Station(_station.Id).CargoPads[CargoShips.Key(other.Number, kept.Number)] = "cargo-1";
        hugger.GlobalTransform = kept.GlobalTransform * kept.Rest(hugger).Translated(new Vector3(0, 2, 0));
        hugger.LinearVelocity = Vector3.Zero;
        await Seconds(2);
        Check("a pad a cargo ship has is not taken from it", kept.HeldShip == null);

        GD.Print(_failures == 0 ? "ALL PASSED" : $"{_failures} FAILED");
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GetTree().Quit(_failures == 0 ? 0 : 1);
    }

    private float _up;

    private Vector3 Out(float metres) => _dock.GlobalTransform * new Vector3(0, 0, metres);

    private ShipBody Ship(string name, Vector3 at, bool radio = true)
    {
        var ship = new ShipBody { Name = name, Spec = GD.Load<ShipSpec>("res://assets/ship/hulls/hull_fighter.tres"), Position = at };
        ship.AddChild(new ShipComputer { Name = "ShipComputer", PowerOnAtStart = false });
        ship.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(5, 3, 10) } });
        ship.AddChild(new RadioDevice { Name = "Radio", Powered = radio });
        ship.AddChild(new ShipHull { Name = "Hull", Insured = false });
        ship.AddChild(new ShipSignature { Name = "Signature" });
        ship.ControlsSource = () => new PilotControls { Up = _up };
        AddChild(ship);
        ship.GlobalBasis = _dock.GlobalBasis;
        return ship;
    }

    private static RadioDevice Radio(ShipBody ship) => ship.Find<RadioDevice>();

    private static string Last(ShipBody ship) => Radio(ship).Heard.Count > 0 ? Radio(ship).Heard[^1].Text : "";

    private async Task Seconds(double seconds)
    {
        for (int i = 0; i < Math.Max(1, (int)Math.Round(seconds * Engine.PhysicsTicksPerSecond)); i++)
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
    }

    private async Task<bool> Until(Func<bool> done, double seconds)
    {
        for (int i = 0; i < seconds * Engine.PhysicsTicksPerSecond; i++)
        {
            if (done())
                return true;
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        }
        return false;
    }

    private void Check(string what, bool ok)
    {
        GD.Print((ok ? "ok   " : "FAIL ") + what);
        if (!ok)
            _failures++;
    }
}
