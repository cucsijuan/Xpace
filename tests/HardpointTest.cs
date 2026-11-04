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
/// Hardpoints: what a ship's hull comes with from the factory is mounted on them, and a pilot's record can change it.
/// With the player's ship:
///
///   - as it came: a laser on each side of the nose (its model there, firing from its muzzle) and the turret on the
///     spine, on the bus at its place;
///   - a laser on the spine instead of the turret: three lasers fire half as often again as two, and the turret's
///     place on the bus is empty;
///   - a hardpoint takes only what it accepts: no turret on the nose;
///   - with the spine empty, the turret's magazine is none of the pilot's racks; back as it came, the turret is back;
///   - a pylon under each wing: missiles hang on the rails (radar ones first, heat seekers from the back), and each
///     leaves from its own rail, gone from it;
///   - another machine's ship shows what its owner sends (the fitting's key, then the missiles on its rails), looks
///     only; until then, the factory's; a model path that is not a weapon is not taken.
///
/// Run: Godot --headless --fixed-fps 60 --path . res://tests/hardpoint_test.tscn. Exits with 0 if every check passed.
/// </summary>
public partial class HardpointTest : Node
{
    private const string Laser = "res://assets/weapons/lasers/lsr_1.tres", Gun = "res://assets/weapons/turrets/gun_1.tres";
    private int _failures;
    private ShipBody _ship;

    public override async void _Ready()
    {
        AddChild(new Projectiles());
        _ship = GD.Load<PackedScene>("res://scenes/ships/player_ship.tscn").Instantiate<ShipBody>();
        AddChild(_ship);
        await Frames(2);
        _ship.ProcessMode = ProcessModeEnum.Disabled;             // the test steps the laser itself
        ShipBus bus = _ship.Computer.Bus;
        var laser = _ship.Device<LaserDevice>();

        Hardpoint left = Point("NoseLeft"), right = Point("NoseRight"), spine = Point("Spine");
        Check($"the ship has its five hardpoints ({_ship.Hardpoints.Count})", _ship.Hardpoints.Count == 5 && left != null && right != null && spine != null);
        Check("as it came: a laser on each side of the nose, its model there", left.Mounted is LaserSpec && right.Mounted is LaserSpec
              && left.Emitter?.Name == "Muzzle" && left.GetNodeOrNull("Laser/Model") != null);
        Check("the turret on the spine, the ship's turret", spine.Turret != null && _ship.Device<TurretDevice>() == spine.Turret);
        laser.BeforeStep(_ship, Step);
        spine.Turret.BeforeStep(_ship, Step);
        Check($"two lasers ({laser.Emitters}); the laser and the turret at their places on the bus",
              laser.Emitters == 2 && bus[LaserDevice.Slot, 0] == LaserDevice.Type && bus[TurretDevice.Slot, 0] == TurretDevice.Type);
        int two = Pulses(laser, 1.0);

        // Missiles hang on the pylons' rails and leave from them
        var launcher = _ship.Device<LauncherDevice>();
        Hardpoint wingLeft = Point("WingLeft"), wingRight = Point("WingRight");
        int Loaded() => launcher.Rails.Count(r => r.GetNodeOrNull("Load") != null);
        string On(int rail) => launcher.Rails[rail].GetNodeOrNull("Load")?.GetMeta("look", "").AsString() ?? "";
        Check($"a pylon under each wing: {launcher.Rails.Count} rails, a missile on each ({Loaded()})",
              wingLeft.Mounted is PylonSpec && wingRight.Mounted is PylonSpec && launcher.Rails.Count == 8 && Loaded() == 8);
        Check($"radar missiles from the first rail on, heat seekers from the last back, the wings in turn ({On(0).GetFile()}, {On(7).GetFile()})",
              launcher.Rails[0].GetParent().GetParent() == wingLeft && launcher.Rails[1].GetParent().GetParent() == wingRight
              && On(0).Contains("msl_1") && On(3).Contains("msl_1") && On(4).Contains("ir_1") && On(7).Contains("ir_1"));
        Node3D next = launcher.Rails[4];
        Vector3 railAt = next.GlobalPosition;
        launcher.BeforeStep(_ship, Step);
        bus.Out(LauncherDevice.Slot, 6, 2);
        launcher.AfterStep(_ship, Step);
        Missile fired = launcher.Newest;
        Check($"a heat seeker leaves from its rail ({(fired != null ? fired.GlobalPosition.DistanceTo(railAt) : -1):0.00} m off), and is gone from it",
              fired != null && fired.GlobalPosition.DistanceTo(railAt) < 0.01 && next.GetNodeOrNull("Load") == null && Loaded() == 7 && launcher.HeatSeekersLeft == 3);
        fired?.QueueFree();

        var record = new ShipRecord { Hull = _ship.Spec.ResourcePath };
        record.Fittings["Spine"] = Laser;
        _ship.Fit(Fittings.Of(_ship.Spec, record));
        laser.BeforeStep(_ship, Step);
        Check($"a laser on the spine instead: three lasers ({laser.Emitters}), no turret", laser.Emitters == 3 && spine.Mounted is LaserSpec
              && spine.Turret == null && _ship.Device<TurretDevice>() == null);
        Check("the turret's place on the bus is empty", Enumerable.Range(0, ShipBus.SlotSize).All(i => bus[TurretDevice.Slot, i] == 0));
        int three = Pulses(laser, 1.0);
        Check($"three fire half as often again as two ({two} and {three} pulses in a second)", two > 0 && Math.Abs(three - two * 1.5) <= 1);

        var pilot = new PilotRecord { Ship = record };
        Check($"no turret, no rounds to carry ({Universe.RackSize(pilot, GD.Load<AmmoSpec>("res://assets/weapons/ammo/rounds_1.tres"))})",
              Universe.RackSize(pilot, GD.Load<AmmoSpec>("res://assets/weapons/ammo/rounds_1.tres")) == 0);

        record.Fittings["NoseLeft"] = Gun;
        _ship.Fit(Fittings.Of(_ship.Spec, record));
        Check($"no turret on the nose: it takes fixed weapons only ({left.Mounted?.ResourcePath ?? "nothing"})", left.Mounted == null && _ship.Device<TurretDevice>() == null);

        record.Fittings.Clear();
        _ship.Fit(Fittings.Of(_ship.Spec, record));
        laser.BeforeStep(_ship, Step);
        _ship.Device<TurretDevice>()?.BeforeStep(_ship, Step);
        Check($"as it came again: two lasers ({laser.Emitters}) and the turret, on the bus", laser.Emitters == 2 && spine.Turret != null
              && _ship.Device<TurretDevice>() == spine.Turret && bus[TurretDevice.Slot, 0] == TurretDevice.Type);
        Check($"its magazine is the pilot's rack again ({Universe.RackSize(pilot, GD.Load<AmmoSpec>("res://assets/weapons/ammo/rounds_1.tres"))})",
              Universe.RackSize(pilot, GD.Load<AmmoSpec>("res://assets/weapons/ammo/rounds_1.tres")) == spine.Turret.Magazine);

        // Another machine's ship
        _ship.Fit(new Dictionary<string, Resource> { ["NoseLeft"] = GD.Load<Resource>(Laser), ["NoseRight"] = GD.Load<Resource>(Laser), ["Spine"] = GD.Load<Resource>(Laser) });
        string key = _ship.Fitted;
        var remote = GD.Load<PackedScene>("res://scenes/ships/remote_fighter.tscn").Instantiate<Xpace.Net.RemoteShip>();
        AddChild(remote);
        Hardpoint Remote(string name) => remote.FindChildren("*", "", true, false).OfType<Hardpoint>().First(h => h.Name == name);
        Check("another machine's ship shows the factory's fit until its owner says", Remote("Spine").Mounted is TurretSpec && Remote("NoseLeft").Mounted is LaserSpec);
        remote.Fit(key);
        Check($"then what its owner has: three lasers ({key})", Remote("Spine").Mounted is LaserSpec && Remote("Spine").GetNodeOrNull("Laser") != null
              && remote.FindChildren("*", "", true, false).OfType<TurretDevice>().Count() == 0);
        remote.Fit("NoseLeft=res://assets/ship/hulls/hull_fighter.tres;Spine=");
        Check("a path that is not a weapon is not taken: that hardpoint shows nothing", Remote("NoseLeft").Mounted == null && Remote("Spine").Mounted == null && Remote("NoseRight").Mounted == null);
        var late = GD.Load<PackedScene>("res://scenes/ships/remote_fighter.tscn").Instantiate<Xpace.Net.RemoteShip>();
        late.Fit(key);
        AddChild(late);
        Check("a ship made after the fit was heard shows it from the start",
              late.FindChildren("*", "", true, false).OfType<Hardpoint>().Where(h => !h.Name.ToString().StartsWith("Wing")).All(h => h.Mounted is LaserSpec));
        Check($"as it came from the factory, another machine's ship shows its pylons full ({late.Rails.Count} rails)",
              remote.Rails.Count == 0 && GD.Load<PackedScene>("res://scenes/ships/remote_fighter.tscn").Instantiate<Xpace.Net.RemoteShip>() is var fresh
              && AddedRails(fresh) == (8, 8));
        remote.Fit("WingLeft=res://assets/weapons/pylons/pyl_1.tres;WingRight=res://assets/weapons/pylons/pyl_1.tres"
                   + "|res://assets/weapons/missiles/msl_1.tres=1;res://assets/weapons/missiles/ir_1.tres=2");
        Check($"and what its owner says hangs on the rails: one radar missile first, two heat seekers last ({remote.Rails.Count(r => r.GetNodeOrNull("Load") != null)})",
              remote.Rails.Count == 8 && remote.Rails.Count(r => r.GetNodeOrNull("Load") != null) == 3
              && remote.Rails[0].GetNode("Load").GetMeta("look").AsString().Contains("msl_1") && remote.Rails[7].GetNode("Load").GetMeta("look").AsString().Contains("ir_1")
              && remote.Rails[6].GetNodeOrNull("Load") != null && remote.Rails[1].GetNodeOrNull("Load") == null);

        GD.Print(_failures == 0 ? "ALL PASSED" : $"{_failures} FAILED");
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GetTree().Quit(_failures == 0 ? 0 : 1);
    }

    private const double Step = 1.0 / 60;

    // A copy of another machine's ship, added: its rails and the missiles on them
    private (int Rails, int Loaded) AddedRails(Xpace.Net.RemoteShip ship)
    {
        AddChild(ship);
        return (ship.Rails.Count, ship.Rails.Count(r => r.GetNodeOrNull("Load") != null));
    }

    private Hardpoint Point(string name) => _ship.Hardpoints.FirstOrDefault(h => h.Name == name);

    // Pulses the laser fires in so many seconds with the trigger held (cool, and with the power it needs)
    private int Pulses(LaserDevice laser, double seconds)
    {
        int before = laser.PulsesFired;
        var power = _ship.Device<PowerDevice>();
        for (int i = 0; i < seconds / Step; i++)
        {
            power?.Rearm();
            _ship.Computer.Bus.Out(LaserDevice.Slot, 4, 1);
            laser.AfterStep(_ship, Step);
        }
        int pulses = laser.PulsesFired - before;
        for (int i = 0; i < 600; i++)                    // let it cool
            laser.AfterStep(_ship, Step);
        return pulses;
    }

    private async Task Frames(int n)
    {
        for (int i = 0; i < n; i++)
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
    }

    private void Check(string what, bool ok)
    {
        GD.Print($"{(ok ? "ok  " : "FAIL")} {what}");
        if (!ok)
            _failures++;
    }
}
