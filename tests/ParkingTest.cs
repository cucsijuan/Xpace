using System;
using System.Threading.Tasks;
using Godot;
using Xpace.Computer;
using Xpace.Ship;
using Xpace.Space;

namespace Xpace.Tests;

/// <summary>
/// The radio, a station's laws and FLIGHT's parking mode, on a ship whose
/// computer runs COS with FLIGHT.COM and COMMS.COM, 1 km from a station
/// whose laws set 30 m/s within 3 km:
///
///   - with the radio off as the ship comes into the zone it misses the
///     laws; COMMS shows the radio off, then, on, the station and no laws;
///   - parking asks the station for them (FLIGHT through the radio), they
///     come, and full throttle then holds the speed limit (half throttle,
///     half of it; full strafe, no more than it); off, the ship goes faster;
///   - outside the zone the limit no longer holds (OPEN SPACE on COMMS), and
///     back inside it holds again, sent as the ship comes in.
///
/// Run: Godot --headless --fixed-fps 60 --path . res://tests/parking_test.tscn
/// Exits with 0 if every check passed.
/// </summary>
public partial class ParkingTest : Node3D
{
    private const float Limit = 30;

    private ShipBody _ship;
    private RadioDevice _radio;
    private DisplayDevice _display;
    private StationRadio _station;
    private PilotControls _controls;
    private int _failures;

    public override async void _Ready()
    {
        var station = new Node3D { Name = "Haven", Position = new Vector3(0, 0, 1000) };
        _station = new StationRadio { Name = "Radio", Laws = new StationLaws { SpeedLimit = Limit, ZoneRadius = 3000 } };
        station.AddChild(_station);
        AddChild(station);

        _ship = new ShipBody { Spec = GD.Load<ShipSpec>("res://assets/ship/hulls/hull_fighter.tres") };
        _ship.AddChild(new ShipComputer { Name = "ShipComputer" });
        _ship.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(5, 3, 10) } });
        _display = new DisplayDevice
        {
            Screens = new Godot.Collections.Array<DisplayScreen>
            {
                GD.Load<DisplayScreen>("res://assets/ship/displays/flight.tres"),
                GD.Load<DisplayScreen>("res://assets/ship/displays/comms.tres"),
            },
        };
        _ship.AddChild(_display);
        _radio = new RadioDevice { Name = "Radio", Callsign = "TESTER", Powered = false };
        _ship.AddChild(_radio);
        _ship.ControlsSource = () => _controls;
        AddChild(_ship);

        Check("COMMS.COM is resident", await Until(() => _ship.Computer.ScreenText().Contains("Radio resident"), 60));
        await Seconds(0.5);
        Check($"the radio off: COMMS says so\n{Comms()}", Comms().Contains("RADIO OFF") && _radio.Laws == null);

        _radio.Powered = true;
        await Seconds(0.5);
        Check($"on, it hears the station but missed its laws\n{Comms()}",
              Comms().Contains("HAVEN") && Comms().Contains("STATION") && Comms().Contains("NO LAWS") && _radio.Laws == null);

        await Press();
        Check("parking asks the station for its laws, and they come", await Until(() => _ship.ParkingLimited, 3));
        await Seconds(0.5);
        Check($"COMMS shows the limit and whose\n{Comms()}", Comms().Contains("SPEED LIMIT 30 m/s") && Comms().Contains("LAWS"));
        Check($"FLIGHT's page shows it\n{_display.Text("FLIGHT")}", _display.Text("FLIGHT").Contains("30 m/s"));

        float full = await Speed(new PilotControls { Throttle = 1 }, 10);
        Check($"parking, full throttle: {full:0.0} m/s, the limit {Limit}", full > Limit - 2 && full <= Limit + 1);
        float half = await Speed(new PilotControls { Throttle = 0.5f }, 10);
        Check($"half throttle: {half:0.0} m/s", half > Limit / 2 - 2 && half < Limit / 2 + 2);
        float side = await Speed(new PilotControls { Right = 1 }, 8);
        Check($"full strafe: {side:0.0} m/s", side > Limit - 3 && side <= Limit + 1);

        await Press();
        Check("parking off", !_ship.ParkingOn);
        float free = await Speed(new PilotControls { Throttle = 1 }, 10);
        Check($"off, full throttle: {free:0} m/s", free > Limit * 3);

        await Press();
        Place(new Vector3(0, 0, 8000));
        await Seconds(1);
        Check($"outside the zone parking keeps no limit\n{Comms()}", _ship.ParkingOn && !_ship.ParkingLimited && Comms().Contains("OPEN SPACE"));
        Place(new Vector3(0, 0, -500));
        await Seconds(1);
        Check($"back inside, the laws hold again\n{Comms()}", _ship.ParkingLimited && Comms().Contains("SPEED LIMIT 30"));

        GD.Print(_failures == 0 ? "ALL PASSED" : $"{_failures} FAILED");
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GetTree().Quit(_failures == 0 ? 0 : 1);
    }

    private string Comms() => _display.Text("COMMS");

    private void Place(Vector3 at) => _ship.State = new ShipState { Position = at, Orientation = Quaternion.Identity };

    /// <summary>The speed after holding the controls that long, from rest.</summary>
    private async Task<float> Speed(PilotControls controls, double seconds)
    {
        Place(Vector3.Zero);
        _controls = controls;
        await Seconds(seconds);
        float v = _ship.LinearVelocity.Length();
        _controls = default;
        Place(Vector3.Zero);
        await Seconds(0.2);
        return v;
    }

    private async Task Press()
    {
        _controls = new PilotControls { Parking = true };
        await Seconds(0.1);
        _controls = default;
        await Seconds(0.1);
    }

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
        GD.Print(_ship.Computer.ScreenText());
        return false;
    }

    private void Check(string what, bool ok)
    {
        GD.Print((ok ? "ok   " : "FAIL ") + what);
        if (!ok)
            _failures++;
    }
}
