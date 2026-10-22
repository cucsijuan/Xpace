using System;
using System.Threading.Tasks;
using Godot;
using Xpace.Computer;
using Xpace.Ship;
using Xpace.Space;

namespace Xpace.Tests;

/// <summary>
/// The turret while the ship turns (FIRECTL turns its filter with the gyros),
/// and the floating origin moving the world back under a fight.
/// Run: Godot --headless --fixed-fps 60 --path . res://tests/maneuver_test.tscn
/// Exits with 0 if every check passed.
/// </summary>
public partial class ManeuverTest : FloatingOrigin
{
    private ShipBody _ship;
    private RadarDevice _radar;
    private TurretDevice _turret;
    private DisplayDevice _display;
    private Drone _drone;
    private PilotControls _controls;
    private int _failures;

    public override async void _Ready()
    {
        AddChild(new Projectiles());
        _ship = new ShipBody { Name = "Ship" };
        _ship.AddChild(new ShipComputer { Name = "ShipComputer" });
        _ship.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(5, 3, 10) } });
        _display = new DisplayDevice();
        _ship.AddChild(_display);
        _turret = new TurretDevice { Name = "Turret", Position = new Vector3(0.9f, 0.8f, -3f) };
        _ship.AddChild(_turret);
        _radar = new RadarDevice { Name = "Radar", Position = new Vector3(0, 0.5f, -5f), TrackOriginPath = "../Turret" };
        _ship.AddChild(_radar);
        _ship.ControlsSource = () => _controls;
        AddChild(_ship);
        ShipPath = "Ship";
        base._Ready();

        // A drone 1.5 km out, 25 degrees to the left and a little up, flying straight
        _drone = new Drone { Straight = true, Evades = false, Speed = 100, Health = 1e9f, Position = new Vector3(-630, 250, -1350) };
        AddChild(_drone);
        _drone.Fly(Vector3.Back * 100);

        Check("the ship's software is running", await Until(() => _display.Text(1).Contains("FIRE CONTROL"), 40));
        await Seconds(1.2);
        _controls.TargetAhead = true;
        await Seconds(0.1);
        _controls.TargetAhead = false;
        Check("locked", await Until(() => _radar.Locked == _drone, 1));
        await Seconds(1);

        // Yaw left across the target while firing: it sweeps from left to right
        _controls.Yaw = -0.4f;
        await Seconds(0.5);
        int rounds0 = _turret.RoundsFired, hits0 = _drone.Hits;
        _controls.Trigger = true;
        await Seconds(4);
        _controls.Trigger = false;
        int rounds = _turret.RoundsFired - rounds0;
        await Seconds(1.8);
        int hits = _drone.Hits - hits0;
        _controls.Yaw = 0;
        Check($"turning at {Mathf.RadToDeg(0.4f * 0.6f):0} deg/s, it keeps firing ({rounds} of 40 rounds)", rounds >= 32);
        Check($"and hitting ({hits} hits, {(rounds == 0 ? 0 : (float)hits / rounds):P0})", hits >= rounds * 0.4f);
        await Seconds(2);

        // Turn the target out of the radar's cone and back: the lock comes back by itself
        _controls.Yaw = -1;
        bool lost = await Until(() => _radar.Locked == null, 4);
        _controls.Yaw = 1;
        await Seconds(0.8);
        _controls.Yaw = 0;
        Check($"out of the cone the lock goes ({lost}), and back in it returns by itself", lost && await Until(() => _radar.Locked == _drone, 3));
        await Seconds(1);

        // 40 km out: the next step brings the world back, under the fight
        float before = _ship.GlobalPosition.DistanceTo(_drone.GlobalPosition);
        Shift(new Vector3(-40_000, 0, 0));
        await Seconds(0.1);
        float after = _ship.GlobalPosition.DistanceTo(_drone.GlobalPosition);
        Check($"the ship is back near the origin ({_ship.GlobalPosition.Length():0} m from it)", _ship.GlobalPosition.Length() < 50);
        Check($"the drone kept its place relative to the ship ({before:0.0} m, then {after:0.0} m)", MathF.Abs(after - before) < 15);
        Check("the radar kept its lock", _radar.Locked == _drone);
        hits0 = _drone.Hits;
        _controls.Trigger = true;
        await Seconds(2);
        _controls.Trigger = false;
        await Seconds(1.8);
        Check($"rounds still hit after the shift ({_drone.Hits - hits0} hits)", _drone.Hits - hits0 >= 5);

        GD.Print(_failures == 0 ? "ALL PASSED" : $"{_failures} FAILED");
        GetTree().Quit(_failures == 0 ? 0 : 1);
    }

    private async Task Seconds(double seconds)
    {
        for (int i = 0; i < (int)Math.Round(seconds * Engine.PhysicsTicksPerSecond); i++)
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
        GD.Print(_display.Text(1));
        return false;
    }

    private void Check(string what, bool ok)
    {
        GD.Print($"{(ok ? "ok  " : "FAIL")} {what}");
        if (!ok)
            _failures++;
    }
}
