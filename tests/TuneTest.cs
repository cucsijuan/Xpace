using System;
using System.Threading.Tasks;
using Godot;
using Xpace.Cockpit;
using Xpace.Ship;
using Xpace.Space;
using Xpace.World;

namespace Xpace.Tests;

/// <summary>
/// Against an interdictor's beam, in the game's main scene with the first sector's buoy:
///
///   - left alone, the generator holds on for a few seconds against the beam, then drops out;
///   - TUNE.COM takes the RADAR screen while the beam is on: the two waves and the bars are drawn on it, and the
///     screen goes back when the beam goes;
///   - the pilot's real keys reach the generator, through TUNE.COM;
///   - a pilot holding the tuning buttons (here, following the beam as a person reads the figure) goes through the
///     beam to the far side still in cruise;
///   - a mass drops the ship at once, as ever: tuning is no way past it.
///
/// Run: Godot --headless --fixed-fps 60 --path . res://tests/tune_test.tscn (XPACE_TUNE_SHOT=file.png saves the
/// RADAR picture once off and once tuned, _off and _on before the extension). Exits with 0 if every check passed.
/// </summary>
public partial class TuneTest : Node
{
    private int _failures;
    private ShipBody _ship;
    private CruiseDevice _cruise;
    private PilotControls _controls;
    private InterdictorBuoy _buoy;

    public override async void _Ready()
    {
        Universe.Current.Forget();
        var main = GD.Load<PackedScene>("res://scenes/main.tscn").Instantiate<SectorHost>();
        AddChild(main);
        _ship = main.Ship;
        _cruise = _ship.GetNode<CruiseDevice>("Cruise");
        _buoy = main.Sector.GetNode<InterdictorBuoy>("InterdictorBuoy");
        _controls = new PilotControls();
        Func<PilotControls> keys = _ship.ControlsSource;
        _ship.ControlsSource = () => _controls;
        foreach (string other in new[] { "Drone1", "Drone2", "Raider" })
            main.Sector.GetNodeOrNull(other)?.QueueFree();
        var display = _ship.GetNode<DisplayDevice>("Display");
        Check("the computer boots", await Until(() => _ship.Computer.ScreenText().Contains("Radio resident"), 90));

        // Left alone
        Vector3 from = Approach();
        double beamAt = -1, droppedAt = -1, t = 0;
        await Cruising(from);
        for (; t < 90 && _cruise.On; t += 1.0 / 60)
        {
            if (beamAt < 0 && _cruise.Locked)
                beamAt = t;
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        }
        droppedAt = t;
        float held = (float)(droppedAt - beamAt);
        Check($"left alone it drops out ({_ship.GlobalPosition.DistanceTo(_buoy.GlobalPosition) / 1000:0} km from the buoy)", _cruise.DroppedOut && !_cruise.On);
        Check($"not at once: it held on {held:0.0} s past what it can bear", beamAt >= 0 && held >= _cruise.Spec.HoldSeconds * 0.9f);
        Check("the screen goes back when the beam goes", await Until(() => !_cruise.BeamOn && Lent(display) == 0, 3));

        // The real keys, through the program. Off frequency, the picture is two different waves
        from = Approach();
        await Cruising(from);
        await Until(() => _cruise.BeamOn, 60);
        await Seconds(1.5);
        Shot(display, "_off");
        float tunedAt = _cruise.Tune;
        _ship.ControlsSource = keys;
        Input.ActionPress(FlightInput.TuneUp);
        await Seconds(0.5);
        Input.ActionRelease(FlightInput.TuneUp);
        _ship.ControlsSource = () => _controls;
        Check($"the pilot's key tunes the generator ({tunedAt:0} to {_cruise.Tune:0})", _cruise.Tune > tunedAt + 15);
        await Until(() => !_cruise.On, 60);

        // A pilot tuning: the buttons, as near as the waves let a person keep it
        from = Approach();
        await Cruising(from);
        bool pageSeen = false;
        float worstStrain = 0, sumCoupling = 0;
        int beamSteps = 0;
        float closest = float.MaxValue;
        bool passed = false;
        double reaction = 0;
        for (t = 0; t < 120 && _cruise.On && !passed; t += 1.0 / 60)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            float d = _ship.GlobalPosition.DistanceTo(_buoy.GlobalPosition);
            closest = Mathf.Min(closest, d);
            passed = d > closest + 40000;
            if (!_cruise.BeamOn)
            {
                _controls.TuneUp = _controls.TuneDown = false;
                continue;
            }
            reaction -= 1.0 / 60;
            if (reaction < 0.45)                // a short press, then a look at what it did
                _controls.TuneUp = _controls.TuneDown = false;
            if (reaction <= 0)                  // a person looks and corrects, not quickly
            {
                reaction = 0.6;
                float off = _cruise.BeamBand - _cruise.Tune;
                _controls.TuneUp = off > 8;
                _controls.TuneDown = off < -8;
            }
            beamSteps++;
            sumCoupling += _cruise.Coupling;
            worstStrain = Mathf.Max(worstStrain, _cruise.Strain);
            if (!pageSeen && beamSteps > 200)
            {
                pageSeen = Lent(display) == 'T' && LitPixels(display) > 30;
                Shot(display, "_on");
            }
        }
        _controls.TuneUp = _controls.TuneDown = false;
        Check($"TUNE has the RADAR screen, the waves on it ({LitPixels(display)} pixels lit)", pageSeen);
        Check($"a pilot tuning goes through in cruise (closest {closest / 1000:0} km, cancelling {sumCoupling / Mathf.Max(beamSteps, 1):P0} on average, worst strain {worstStrain:P0})",
              passed && _cruise.On && closest < 26000);
        Check("the screen goes back past it", await Until(() => Lent(display) == 0, 30));

        // A mass is another matter
        _buoy.QueueFree();
        Node3D station = main.Sector.GetNode<Node3D>("Station");
        Point(station.GlobalPosition + new Vector3(1, 0, 0.3f).Normalized() * 60000, station.GlobalPosition, 0);
        _cruise.Rearm();
        await Cruising(_ship.GlobalPosition);
        Check("a mass drops it out, past any tuning, without the strain's seconds", await Until(() => _cruise.DroppedOut, 90) &&
              _ship.GlobalPosition.DistanceTo(station.GlobalPosition) > 4000);

        GD.Print(_failures == 0 ? "ALL PASSED" : $"{_failures} FAILED");
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GetTree().Quit(_failures == 0 ? 0 : 1);
    }

    // Beyond the buoy's reach, towards it and on past it
    private Vector3 Approach()
    {
        Vector3 at = _buoy.GlobalPosition + new Vector3(1, 0, -1).Normalized() * (_buoy.Range + 8000);
        Point(at, _buoy.GlobalPosition + new Vector3(1, 0, 1).Normalized() * 15000, 0);
        _ship.Device<CruiseDevice>().Rearm();
        return at;
    }

    private async Task Cruising(Vector3 from)
    {
        _controls.Throttle = 1;
        await Seconds(0.3);
        _controls.Cruise = true;
        await Seconds(0.1);
        _controls.Cruise = false;
        Check($"(cruising; density {_cruise.Density:0.0})", await Until(() => _cruise.On, 8));
    }

    private void Point(Vector3 at, Vector3 to, float speed)
    {
        Basis facing = Basis.LookingAt((to - at).Normalized(), Vector3.Up);
        _ship.State = new ShipState
        {
            Position = at, Orientation = facing.GetRotationQuaternion(),
            Velocity = (to - at).Normalized() * speed, AngularVelocity = Vector3.Zero,
        };
    }

    private static byte Lent(DisplayDevice display) => display.Lent("RADAR");

    private static int LitPixels(DisplayDevice display)
    {
        int lit = 0;
        for (int y = 0; y < 128; y++)
            for (int x = 0; x < 160; x++)
                if (display.Pixel("RADAR", x, y) != 0)
                    lit++;
        return lit;
    }

    private static void Shot(DisplayDevice display, string which)
    {
        if (OS.GetEnvironment("XPACE_TUNE_SHOT") is { Length: > 0 } file)
            display.Picture("RADAR")?.SavePng(file.Replace(".png", which + ".png"));
    }

    private async Task Seconds(double s) => await Until(() => false, s);

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
}
