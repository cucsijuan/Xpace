using System;
using System.Threading.Tasks;
using Godot;
using Xpace.Computer;
using Xpace.Ship;

namespace Xpace.Tests;

/// <summary>
/// The ship flown by FLIGHT.COM on its computer, step by step. What full
/// stick and full throttle ask for is the ship's own top (its rating plate:
/// its thrusters against the veil), never a number in the program.
/// Run: Godot --headless --fixed-fps 60 --path . res://tests/flight_test.tscn
/// Exits with 0 if every check passed.
/// </summary>
public partial class FlightTest : Node
{
    private ShipBody _ship;
    private PilotControls _controls;
    private DisplayDevice _display;
    private int _failures;

    public override async void _Ready()
    {
        _ship = new ShipBody();
        _ship.AddChild(new ShipComputer { Name = "ShipComputer" });
        _display = new DisplayDevice { Screens = DisplayScreens() };
        _ship.AddChild(_display);
        _ship.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(5, 3, 10) } });
        _ship.ControlsSource = () => _controls;
        AddChild(_ship);

        Check("before the program runs, the thrusters follow the controls", _ship.DirectMode);
        Check("AUTOEXEC.BAT starts FLIGHT.COM", await Until(() => _ship.AssistOn, 30));
        Check("its page is on the FLIGHT screen", _display.Text("FLIGHT").Contains("FLIGHT CONTROL"));
        Check("it stays resident: the console is back", await Until(() => _ship.Computer.ScreenText().Contains("A:\\>"), 5));

        // Flight assist: the stick asks for a turn rate, of the top the plate gives
        Vector3 top = _ship.Spec.TopTurnRate();
        float topSpeed = _ship.Spec.TopSpeed(_ship.Spec.MainThrust);
        Check($"the rating plate gives the tops: {Plate(14) / 8f:0} m/s ahead, {Plate(22) / 1024f:0.00} {Plate(24) / 1024f:0.00} {Plate(26) / 1024f:0.00} rad/s",
              MathF.Abs(Plate(14) / 8f - topSpeed) < 0.2f && MathF.Abs(Plate(22) / 1024f - top.X) < 0.002f &&
              MathF.Abs(Plate(24) / 1024f - top.Y) < 0.002f && MathF.Abs(Plate(26) / 1024f - top.Z) < 0.002f);
        _controls.Pitch = 0.5f;
        await Seconds(2);
        Check($"half stick: half the top pitch rate, {top.X / 2:0.00} rad/s ({Rate().X:0.00})", Near(Rate().X, top.X / 2, top.X * 0.05f));
        _controls.Pitch = 0;
        await Seconds(1.5);
        Check($"let go: the turn stops ({Rate().X:0.000})", MathF.Abs(Rate().X) < 0.02f);

        _controls.Roll = -1;
        await Seconds(1.5);
        Check($"full left roll: the top roll rate, {-top.Z:0.00} rad/s ({Rate().Y:0.00})", Near(Rate().Y, -top.Z, top.Z * 0.07f));
        _controls.Roll = 0;
        await Seconds(1.5);
        Check($"let go: the roll stops ({Rate().Y:0.000})", MathF.Abs(Rate().Y) < 0.02f);

        // The throttle asks for a forward speed
        _controls.Throttle = 0.4f;
        await Seconds(6);
        Check($"40% throttle: 40% of the top speed, {0.4f * topSpeed:0} m/s ({Velocity().Z:0.0})", Near(Velocity().Z, 0.4f * topSpeed, 4));
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
        float half = 0.5f * _ship.Spec.TurnAcceleration.X, kx = _ship.Spec.VeilSpin.X;
        float pushed = MathF.Sqrt(half / kx) * MathF.Tanh(0.5f * MathF.Sqrt(half * kx));     // half torque against the veil for 0.5 s
        float veil = released / (1 + kx * released * 1);
        Check($"assist off: half stick for 0.5 s, then the ship turns on, slowed only by the veil ({released:0.000} -> {coasting:0.000} rad/s, {veil:0.000} expected)",
              Near(released, pushed, 0.02f) && Near(coasting, veil, 0.01f));
        await Press();
        await Seconds(2);
        Check($"assist on again: the turn stops ({Rate().X:0.000})", _ship.AssistOn && MathF.Abs(Rate().X) < 0.02f);

        // Safe turns: full stick asks only for what the thrusters can bend the path to
        // (the nose stays a few degrees off: the speed hold needs an error to push with)
        _controls.Throttle = 150 / topSpeed;
        await Seconds(8);
        var (freeRate, freeSlip) = await PullUp(2);
        Check($"at 150 m/s, full stick pitches at the top rate, {top.X:0.00} rad/s, and the ship slides on ({freeRate:0.00} rad/s, nose {freeSlip:0} deg off its path)",
              Near(freeRate, top.X, 0.1f) && freeSlip > 30);
        await Seconds(6);
        await PressSafe();
        Check("the SAFE button turns safe turns on", Line(_display.Text("FLIGHT"), "SAFE TURNS").Contains("ON"));
        float expected = 0.75f * _ship.Spec.Vertical / _ship.Spec.Mass / 150;
        var (safeRate, safeSlip) = await PullUp(3);
        Check($"safe: full stick pitches at what the push across allows, {expected:0.000} rad/s ({safeRate:0.000}), the nose on the path ({safeSlip:0.0} deg)",
              Near(safeRate, expected, 0.015f) && safeSlip < 8);
        await Seconds(3);
        _ship.Spec.VerticalThrust *= 2;
        var (strongRate, strongSlip) = await PullUp(3);
        Check($"twice the thrust up and down: twice the safe rate ({strongRate:0.000} rad/s, nose {strongSlip:0.0} deg off)",
              Near(strongRate, 2 * expected, 0.03f) && strongSlip < 8);
        _ship.Spec.VerticalThrust /= 2;
        await PressSafe();
        Check("SAFE again turns them off", Line(_display.Text("FLIGHT"), "SAFE TURNS").Contains("OFF"));
        _controls.Throttle = 0;
        await Seconds(8);

        string load = Line(_display.Text("FLIGHT"), "CPU LOAD");
        GD.Print($"     {load.Trim()}");

        // The console works while the ship flies
        await Type("DIR\n");
        Check("DIR at the console while flying", await Until(() => _ship.Computer.ScreenText().Contains("FLIGHT   COM"), 5) && _ship.AssistOn);

        // FLIGHT OFF: direct mode
        await Type("FLIGHT OFF\n");
        Check("FLIGHT OFF removes it: direct mode", await Until(() => _ship.DirectMode, 5));
        Check("its message is on the console", _ship.Computer.ScreenText().Contains("Flight control removed"));
        _controls.Yaw = 1;
        await Seconds(1);
        _controls.Yaw = 0;
        // Full torque against the veil: w = top tanh(t sqrt(a k))
        float yaw = _ship.Spec.TurnAcceleration.Y, k = _ship.Spec.VeilSpin.Y;
        float yawAfter = top.Y * MathF.Tanh(MathF.Sqrt(yaw * k));
        Check($"direct: full yaw turns at {yaw:0.00} rad/s², held back by the veil ({Rate().Z:0.00} rad/s after 1 s, {yawAfter:0.00} expected)",
              Near(Rate().Z, yawAfter, 0.05f));

        GD.Print(_failures == 0 ? "ALL PASSED" : $"{_failures} FAILED");
        GetTree().Quit(_failures == 0 ? 0 : 1);
    }

    /// <summary>Pitch, roll and yaw rates (rad/s) about the ship's own axes, positive up, right, right.</summary>
    private Vector3 Rate()
    {
        Vector3 w = _ship.GlobalBasis.Inverse() * _ship.AngularVelocity;
        return new Vector3(w.X, -w.Z, -w.Y);
    }

    /// <summary>A word of the whole rating plate in memory (D000:1400).</summary>
    private unsafe int Plate(int offset)
    {
        byte* p = _ship.Computer.Memory + ShipMemory.Base + ShipMemory.Ratings + offset;
        return p[0] | p[1] << 8;
    }

    /// <summary>Right, up and forward speeds (m/s) along the ship's own axes.</summary>
    private Vector3 Velocity()
    {
        Vector3 v = _ship.GlobalBasis.Inverse() * _ship.LinearVelocity;
        return new Vector3(v.X, v.Y, -v.Z);
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
                >= '0' and <= '9' => "Digit" + c,
                _ => throw new ArgumentException($"no key for '{c}'"),
            };
            _ship.Computer.Key(code, true);
            await Seconds(0.05);
            _ship.Computer.Key(code, false);
            await Seconds(0.05);
        }
    }

    /// <summary>Full stick back for a while: the pitch rate at the end and the most the nose got off the ship's path (degrees).</summary>
    private async Task<(float Rate, float Slip)> PullUp(double seconds)
    {
        _controls.Pitch = 1;
        float slip = 0;
        for (int i = 0; i < (int)Math.Round(seconds * Engine.PhysicsTicksPerSecond); i++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            slip = MathF.Max(slip, Mathf.RadToDeg(_ship.LinearVelocity.AngleTo(-_ship.GlobalBasis.Z)));
        }
        float rate = Rate().X;
        _controls.Pitch = 0;
        return (rate, slip);
    }

    private async Task PressSafe()
    {
        _controls.SafeButton = true;
        await Seconds(0.1);
        _controls.SafeButton = false;
        await Seconds(0.2);
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

    // The cockpit's screens, and FLIGHT's page for the tests to read
    private static Godot.Collections.Array<DisplayScreen> DisplayScreens() => new()
    {
        GD.Load<DisplayScreen>("res://assets/ship/displays/flight.tres"),
        GD.Load<DisplayScreen>("res://assets/ship/displays/radar.tres"),
        GD.Load<DisplayScreen>("res://assets/ship/displays/weapon.tres"),
    };
}
