using System;
using System.Threading.Tasks;
using Godot;
using Xpace.Computer;
using Xpace.Ship;
using Xpace.Space;

namespace Xpace.Tests;

/// <summary>
/// The radar and the turret run by FIRECTL.COM against target drones.
/// Run: Godot --headless --fixed-fps 60 --path . res://tests/combat_test.tscn
/// Exits with 0 if every check passed.
/// </summary>
public partial class CombatTest : Node3D
{
    private ShipBody _ship;
    private RadarDevice _radar;
    private TurretDevice _turret;
    private DisplayDevice _display;
    private Drone _crossing, _other;
    private PilotControls _controls;
    private int _failures;

    public override async void _Ready()
    {
        AddChild(new Projectiles());
        _ship = new ShipBody();
        _ship.AddChild(new ShipComputer { Name = "ShipComputer" });
        _ship.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(5, 3, 10) } });
        _display = new DisplayDevice { Screens = DisplayScreens() };
        _ship.AddChild(_display);
        _turret = new TurretDevice { Name = "Turret", Position = new Vector3(0.9f, 0.8f, -3f) };
        _ship.AddChild(_turret);
        _radar = new RadarDevice { Name = "Radar", Position = new Vector3(0, 0.5f, -5f), TrackOriginPath = "../Turret" };
        _ship.AddChild(_radar);
        _ship.ControlsSource = () => _controls;
        AddChild(_ship);

        // A drone crossing 2 km ahead, and another off to the right
        _crossing = new Drone { Straight = true, Evades = false, Speed = 120, Position = new Vector3(-500, 60, -2000) };
        AddChild(_crossing);
        _crossing.LookAt(_crossing.GlobalPosition + Vector3.Right, Vector3.Up);
        _other = new Drone { Straight = true, Evades = false, Speed = 60, Position = new Vector3(1800, -100, -2600) };
        AddChild(_other);
        _other.LookAt(_other.GlobalPosition + Vector3.Back, Vector3.Up);

        Check("FIRECTL.COM goes resident from AUTOEXEC.BAT", await Until(() => _display.Text("WEAPON").Contains("FIRECTL"), 40));
        Check("the radar is on and sees both drones", await Until(() => _ship.Computer.Bus[RadarDevice.Slot, 3] == 2, 3));

        await Press(() => _controls.TargetAhead = true, () => _controls.TargetAhead = false);
        Check("T locks the drone nearest the middle", await Until(() => _radar.Locked == _crossing, 1));

        await Seconds(2);
        float error = Mathf.RadToDeg(_turret.BarrelDirection.AngleTo(Intercept(_crossing)));
        Check($"the turret leads the crossing drone ({error:0.000} degrees off the intercept)", error < 0.25f);

        _controls.Trigger = true;                   // a 1 s burst, then wait for the rounds to arrive
        await Seconds(1);
        _controls.Trigger = false;
        int fired = _turret.RoundsFired;
        await Seconds(2.5);
        int hits = _crossing.Hits;
        Check($"a 1 s burst at 2 km: {hits} hits of {fired} rounds (the radar is noisy now)", fired >= 8 && hits >= 1);
        _crossing.GlobalPosition = new Vector3(-300, 60, -1000);   // closer in, where the gun can kill
        _crossing.Fly(Vector3.Right * 120);
        await Seconds(1.5);
        _controls.Trigger = true;
        bool killed = await Until(() => !_crossing.Alive, 15);
        Check($"firing on, the drone blows up ({_crossing.Hits} hits of {_turret.RoundsFired} rounds, heat {_turret.Heat:0})", killed);
        _controls.Trigger = false;

        await Seconds(1);
        await Press(() => _controls.NextTarget = true, () => _controls.NextTarget = false);
        Check("G locks the next contact", await Until(() => _radar.Locked == _other, 2));

        GD.Print($"     {Line(_display.Text("FLIGHT"), "CPU LOAD").Trim()} (flight), {Line(_display.Text("WEAPON"), "CPU").Trim()} (fire control)");

        await Type("FIRECTL OFF\n");
        Check("FIRECTL OFF turns the radar off", await Until(() => !_radar.On && _radar.Locked == null, 5));

        GD.Print(_failures == 0 ? "ALL PASSED" : $"{_failures} FAILED");
        GetTree().Quit(_failures == 0 ? 0 : 1);
    }

    /// <summary>The direction a round from the turret must leave in to meet the drone.</summary>
    private Vector3 Intercept(Drone drone)
    {
        Vector3 p = drone.GlobalPosition - _turret.GlobalPosition;
        Vector3 v = drone.Velocity - _ship.LinearVelocity;
        float s = _turret.MuzzleSpeed;
        float a = v.Dot(v) - s * s, b = 2 * p.Dot(v), c = p.Dot(p);
        float t = (-b - MathF.Sqrt(b * b - 4 * a * c)) / (2 * a);
        return (p + v * t).Normalized();
    }

    private async Task Press(Action down, Action up)
    {
        down();
        await Seconds(0.1);
        up();
        await Seconds(0.1);
    }

    private async Task Type(string text)
    {
        foreach (char c in text)
        {
            string code = c switch
            {
                '\n' => "Enter",
                ' ' => "Space",
                >= 'A' and <= 'Z' => "Key" + c,
                _ => throw new ArgumentException($"no key for '{c}'"),
            };
            _ship.Computer.Key(code, true);
            await Seconds(0.05);
            _ship.Computer.Key(code, false);
            await Seconds(0.05);
        }
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
        GD.Print(_ship.Computer.ScreenText());
        GD.Print(_display.Text("WEAPON"));
        return false;
    }

    private static string Line(string text, string start)
    {
        foreach (string line in text.Split('\n'))
            if (line.Contains(start))
                return line;
        return "";
    }

    private void Check(string what, bool ok)
    {
        GD.Print($"{(ok ? "ok  " : "FAIL")} {what}");
        if (!ok)
            _failures++;
    }

    // The cockpit's screens, and FLIGHT's page for the tests to read
    private static Godot.Collections.Array<DisplayScreen> DisplayScreens() => new()
    {
        GD.Load<DisplayScreen>("res://assets/ship/displays/flight.tres"),
        GD.Load<DisplayScreen>("res://assets/ship/displays/radar.tres"),
        GD.Load<DisplayScreen>("res://assets/ship/displays/weapon.tres"),
    };
}
