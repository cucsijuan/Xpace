using System;
using System.Threading.Tasks;
using Godot;
using Xpace.Computer;
using Xpace.Ship;

namespace Xpace.Tests;

/// <summary>
/// The ship flown by FLIGHT.COM on its computer, step by step.
/// Run: Godot --headless --fixed-fps 60 --path . res://tests/flight_test.tscn
/// Exits with 0 if every check passed.
/// </summary>
public partial class FlightTest : Node
{
    private ShipBody _ship;
    private PilotControls _controls;
    private int _failures;

    public override async void _Ready()
    {
        _ship = new ShipBody();
        _ship.AddChild(new ShipComputer { Name = "ShipComputer" });
        _ship.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(5, 3, 10) } });
        _ship.ControlsSource = () => _controls;
        AddChild(_ship);

        Check("before the program runs, the thrusters follow the controls", _ship.DirectMode);
        Check("AUTOEXEC.BAT starts FLIGHT.COM", await Until(() => _ship.AssistOn, 30));
        Check("its page is on the screen", _ship.Computer.ScreenText().Contains("CUCSICORP FLIGHT CONTROL"));

        // Flight assist: the stick asks for a turn rate
        _controls.Pitch = 0.5f;
        await Seconds(2);
        Check($"half stick: pitch rate 0.6 rad/s ({Rate().X:0.00})", Near(Rate().X, 0.6f, 0.06f));
        _controls.Pitch = 0;
        await Seconds(1.5);
        Check($"let go: the turn stops ({Rate().X:0.000})", MathF.Abs(Rate().X) < 0.02f);

        _controls.Roll = -1;
        await Seconds(1.5);
        Check($"full left roll: -2.5 rad/s ({Rate().Y:0.00})", Near(Rate().Y, -2.5f, 0.2f));
        _controls.Roll = 0;
        await Seconds(1.5);
        Check($"let go: the roll stops ({Rate().Y:0.000})", MathF.Abs(Rate().Y) < 0.02f);

        // The throttle asks for a forward speed
        _controls.Throttle = 0.4f;
        await Seconds(6);
        Check($"40% throttle: 100 m/s forward ({Velocity().Z:0.0})", Near(Velocity().Z, 100, 4));
        _controls.Throttle = 0;
        await Seconds(6);
        Check($"throttle at zero: the ship stops ({Velocity().Length():0.00} m/s)", Velocity().Length() < 1);

        _controls.Right = 1;
        await Seconds(1);
        float sideways = Velocity().X;
        _controls.Right = 0;
        await Seconds(3);
        Check($"strafe right, then let go: {sideways:0} m/s, then {Velocity().X:0.0}", sideways > 15 && MathF.Abs(Velocity().X) < 1);

        // Flight assist off: the motion stays
        await Press();
        Check("the FA button switches assist off", await Until(() => !_ship.AssistOn, 1));
        _controls.Pitch = 0.5f;
        await Seconds(0.5);
        _controls.Pitch = 0;
        float released = Rate().X;
        await Seconds(1);
        float coasting = Rate().X;
        Check($"assist off: half stick for 0.5 s, then the ship keeps turning ({released:0.000} -> {coasting:0.000} rad/s)",
              Near(released, 0.375f, 0.03f) && Near(coasting, released, 0.01f));
        await Press();
        await Seconds(2);
        Check($"assist on again: the turn stops ({Rate().X:0.000})", _ship.AssistOn && MathF.Abs(Rate().X) < 0.02f);

        string load = Line(_ship.Computer.ScreenText(), "CPU LOAD");
        GD.Print($"     {load.Trim()}");

        // Esc on the computer's keyboard ends FLIGHT: direct mode
        _ship.Computer.Key("Escape", true);
        await Seconds(0.1);
        _ship.Computer.Key("Escape", false);
        Check("Esc ends the program: direct mode", await Until(() => _ship.DirectMode, 2));
        Check("its message is on the screen", _ship.Computer.ScreenText().Contains("Flight control stopped"));
        _controls.Yaw = 1;
        await Seconds(1);
        _controls.Yaw = 0;
        Check($"direct: full yaw turns at 0.8 rad/s² ({Rate().Z:0.00} rad/s after 1 s)", Near(Rate().Z, 0.8f, 0.05f));

        GD.Print(_failures == 0 ? "ALL PASSED" : $"{_failures} FAILED");
        GetTree().Quit(_failures == 0 ? 0 : 1);
    }

    /// <summary>Pitch, roll and yaw rates (rad/s) about the ship's own axes, positive up, right, right.</summary>
    private Vector3 Rate()
    {
        Vector3 w = _ship.GlobalBasis.Inverse() * _ship.AngularVelocity;
        return new Vector3(w.X, -w.Z, -w.Y);
    }

    /// <summary>Right, up and forward speeds (m/s) along the ship's own axes.</summary>
    private Vector3 Velocity()
    {
        Vector3 v = _ship.GlobalBasis.Inverse() * _ship.LinearVelocity;
        return new Vector3(v.X, v.Y, -v.Z);
    }

    private async Task Press()
    {
        _controls.AssistButton = true;
        await Seconds(0.1);
        _controls.AssistButton = false;
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
        GD.Print(_ship.Computer.ScreenText());
        GD.Print($"machine: powered {_ship.Computer.Powered}, error '{_ship.Computer.Error}'");
        return false;
    }

    private static bool Near(float value, float want, float tolerance) => MathF.Abs(value - want) <= tolerance;

    private static string Line(string text, string start)
    {
        foreach (string line in text.Split('\n'))
            if (line.TrimStart().StartsWith(start))
                return line;
        return "";
    }

    private void Check(string what, bool ok)
    {
        GD.Print($"{(ok ? "ok  " : "FAIL")} {what}");
        if (!ok)
            _failures++;
    }
}
