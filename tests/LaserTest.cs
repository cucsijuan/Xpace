using System;
using System.Threading.Tasks;
using Godot;
using Xpace.Computer;
using Xpace.Net;
using Xpace.Ship;
using Xpace.Space;

namespace Xpace.Tests;

/// <summary>
/// The pulse laser and its gunsight, run by FIRECTL.COM: next weapon
/// selects it (the ship has no launcher, so straight from the gun); with a
/// drone locked the program writes the lead point, and it is where a pulse
/// fired now meets the drone. The ship holds still and a drone crosses
/// 800 m ahead, three times each way: fired when the lead circle crosses
/// the pipper, the pulses hit; fired when the drone itself crosses the
/// bore, they pass behind it.
/// Held down, the laser overheats and stops, and cools again. Its pulses
/// are their own kind of round, in a session too.
///
/// Run: Godot --headless --fixed-fps 60 --path . res://tests/laser_test.tscn
/// Exits with 0 if every check passed.
/// </summary>
public partial class LaserTest : Node3D
{
    private const float Range = 800, Crossing = 80;
    private ShipBody _ship;
    private RadarDevice _radar;
    private LaserDevice _laser;
    private DisplayDevice _display;
    private Drone _drone;
    private PilotControls _controls;
    private int _failures;

    public override async void _Ready()
    {
        AddChild(new Projectiles());
        _ship = new ShipBody();
        _ship.AddChild(new ShipComputer { Name = "ShipComputer" });
        _display = new DisplayDevice { Screens = DisplayScreens() };
        _ship.AddChild(_display);
        _ship.AddChild(new TurretDevice { Name = "Turret", Position = new Vector3(3.6f, 0.5f, -6.5f) });
        _radar = new RadarDevice { Name = "Radar", Position = new Vector3(0, 0.5f, -5f), TrackOriginPath = "../Turret" };
        _ship.AddChild(_radar);
        _laser = new LaserDevice { Name = "Laser", Position = new Vector3(0, -0.8f, -5.5f) };
        _laser.AddChild(new Marker3D { Name = "Left", Position = new Vector3(-1, 0, 0) });
        _laser.AddChild(new Marker3D { Name = "Right", Position = new Vector3(1, 0, 0) });
        _ship.AddChild(_laser);
        _ship.ControlsSource = () => _controls;
        AddChild(_ship);
        _drone = new Drone { Straight = true, Evades = false, Defends = false, Speed = Crossing, Health = 100_000 };
        AddChild(_drone);
        Place();

        Check("the ship's software is running", await Until(() => _display.Text("WEAPON").Contains("FIRECTL"), 40));
        await Press(() => _controls.NextWeapon = true, () => _controls.NextWeapon = false);
        Check("next weapon selects the laser", await Until(() => _display.Text("WEAPON").Contains("LSR"), 1));
        Check("with nothing locked there is no lead circle", _laser.LeadPoint == null);

        // The lead point the program writes, against the truth
        await Lock();
        await Seconds(1.5);
        float worst = 0;
        for (int i = 0; i < 60; i++)
        {
            await Seconds(1.0 / 60);
            if (_laser.LeadPoint is not Vector3 lead)
            {
                worst = float.MaxValue;
                break;
            }
            Vector3 truth = Intercept();
            worst = MathF.Max(worst, (lead - _laser.GlobalPosition).AngleTo(truth - _laser.GlobalPosition) * 1000);
        }
        Check($"locked, the lead point is where a pulse meets the drone (within {worst:0.0} mrad, the drone is {8000 / Range:0} mrad long)",
              worst < 4);

        // Fired on the lead circle, and on the drone itself
        int led = 0, naive = 0, ledPulses = 0, naivePulses = 0;
        for (int i = 0; i < 3; i++)
        {
            int start = _laser.PulsesFired;
            led += await Pass(onLead: true);
            ledPulses += _laser.PulsesFired - start;
        }
        for (int i = 0; i < 3; i++)
        {
            int start = _laser.PulsesFired;
            naive += await Pass(onLead: false);
            naivePulses += _laser.PulsesFired - start;
        }
        Check($"fired as the lead circle crosses the pipper, the pulses hit ({led} hits of {ledPulses})", led >= 2 && led * 2 >= ledPulses);
        Check($"fired as the drone itself crosses the bore, they pass behind it ({naive} hits of {naivePulses})", naive == 0 && naivePulses > 0);

        // Held down: too hot, it stops; then it cools
        _controls.Trigger = true;
        bool hot = await Until(() => _laser.TooHot && _display.Text("WEAPON").Contains("LSR TOO HOT"), 15);
        int pulses = _laser.PulsesFired;
        await Seconds(1);
        Check($"held down it overheats and stops ({hot}, {_laser.PulsesFired - pulses} pulses in the next second)", hot && _laser.PulsesFired == pulses);
        _controls.Trigger = false;
        Check("and cools again", await Until(() => !_laser.TooHot, 10));

        // Its pulses are a kind of their own, across a session too
        var e = new FireEvent { Kind = _laser.RoundKind, Views = new() };
        Check("a pulse's kind travels with the round fired", FireEvent.Read(new BitReader(e.ToBytes())).Kind == 1);

        await Press(() => _controls.NextWeapon = true, () => _controls.NextWeapon = false);
        Check("next weapon goes back to the gun", await Until(() => _display.Text("WEAPON").Contains("GUN"), 1));

        GD.Print(_failures == 0 ? "ALL PASSED" : $"{_failures} FAILED");
        GetTree().Quit(_failures == 0 ? 0 : 1);
    }

    /// <summary>The drone 500 m left of the nose, 800 m out, crossing to the right.</summary>
    private void Place()
    {
        _drone.GlobalPosition = new Vector3(-500, -0.8f, -5.5f - Range);
        _drone.LookAt(_drone.GlobalPosition + Vector3.Right, Vector3.Up);
        _drone.Fly(Vector3.Right * Crossing);
    }

    /// <summary>One crossing, firing while the aim is within a few mrad of the bore; the hits it made.</summary>
    private async Task<int> Pass(bool onLead)
    {
        Place();
        await Lock();
        int before = _drone.Hits;
        Vector3 bore = -_laser.GlobalBasis.Z;
        for (int step = 0; step < 9 * 60 && _drone.GlobalPosition.X < 300; step++)
        {
            await Seconds(1.0 / 60);
            Vector3? aim = onLead ? _laser.LeadPoint : _drone.GlobalPosition;
            _controls.Trigger = aim is Vector3 a && (a - _laser.GlobalPosition).AngleTo(bore) * 1000 < 8;
        }
        _controls.Trigger = false;
        await Seconds(1);                               // the last pulses arrive
        GD.Print($"     {(onLead ? "on the lead circle" : "on the drone     ")}: {_drone.Hits - before} hits");
        return _drone.Hits - before;
    }

    /// <summary>Where a pulse from the laser meets the drone.</summary>
    private Vector3 Intercept()
    {
        Vector3 p = _drone.GlobalPosition - _laser.GlobalPosition;
        Vector3 v = _drone.Velocity - _ship.LinearVelocity;
        float s = _laser.BoltSpeed;
        float a = v.Dot(v) - s * s, b = 2 * p.Dot(v), c = p.Dot(p);
        float t = (-b - MathF.Sqrt(b * b - 4 * a * c)) / (2 * a);
        return _laser.GlobalPosition + p + v * t;
    }

    private async Task Lock()
    {
        // (a jump loses the lock a step later: the tracker finds nothing where it had it, and does not
        // look for it a long way off; a press while it is still locked would let it go)
        await Seconds(0.1);
        for (int i = 0; i < 3 && _radar.Locked != _drone; i++)
        {
            if (_radar.Locked != null)
                await Press(() => _controls.TargetAhead = true, () => _controls.TargetAhead = false);   // lets the old lock go
            await Seconds(0.6);                         // a sweep, so the radar has it in its table
            if (_radar.Locked == null)
                await Press(() => _controls.TargetAhead = true, () => _controls.TargetAhead = false);
            await Until(() => _radar.Locked == _drone, 1);
        }
        Check("T locks the drone", _radar.Locked == _drone);
    }

    private async Task Press(Action down, Action up)
    {
        down();
        await Seconds(0.1);
        up();
        await Seconds(0.1);
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
        return done();
    }

    private void Check(string what, bool ok)
    {
        GD.Print($"{(ok ? "ok  " : "FAIL")} {what}");
        if (!ok)
            _failures++;
    }

    private static Godot.Collections.Array<DisplayScreen> DisplayScreens() => new()
    {
        GD.Load<DisplayScreen>("res://assets/ship/displays/flight.tres"),
        GD.Load<DisplayScreen>("res://assets/ship/displays/radar.tres"),
        GD.Load<DisplayScreen>("res://assets/ship/displays/weapon.tres"),
    };
}
