using System;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Xpace.Computer;
using Xpace.Net;
using Xpace.Ship;
using Xpace.Space;
using Xpace.World;

namespace Xpace.Tests;

/// <summary>
/// Cruise in the real game: near a station the generator will not start;
/// COMMS sets a course for the gate; the generator charges, thins the veil
/// and takes over from the engine, which rests; the plate reads the new tops
/// and FLIGHT flies to them; the ship crosses the sector, slowing by itself
/// as the medium thickens, and drops out near the gate, where its radio
/// reaches it. Stopped at full speed the veil brakes the ship no harder than
/// the generator's limit. Its nose turned, the path follows. A cruising
/// ship's speed fits a snapshot.
///
/// Run: Godot --headless --fixed-fps 60 --path . res://tests/cruise_test.tscn
/// Exits with 0 if every check passed.
/// </summary>
public partial class CruiseTest : Node
{
    private int _failures;
    private SectorHost _main;
    private ShipBody _ship;
    private CruiseDevice _cruise;
    private PilotControls _controls;

    public override async void _Ready()
    {
        Universe.Current.Forget();
        _main = GD.Load<PackedScene>("res://scenes/main.tscn").Instantiate<SectorHost>();
        AddChild(_main);
        _ship = _main.Ship;
        _cruise = _ship.Find<CruiseDevice>();
        var radio = _ship.Find<RadioDevice>();
        var display = _ship.Find<DisplayDevice>();
        var engine = _ship.Find<EngineDevice>();
        foreach (string ship in new[] { "Drone1", "Drone2", "Raider", "InterdictorBuoy" })
            _main.Sector.GetNodeOrNull(ship)?.QueueFree();
        _ship.ControlsSource = () => _controls;
        JumpGate gate = _main.Sector.GetNode<JumpGate>("Gate2");
        Node3D station = _main.Sector.GetNode<Node3D>("Station");

        Check("the software is running", await Until(() => _ship.Computer.ScreenText().Contains("Comm") || radio.Contacts.Count > 1, 90));
        await Seconds(25);
        Check("the gate is heard across the sector", radio.Contacts.Contains(gate.Radio));

        // Too near the station to thin anything
        Check($"by the station the medium is too dense (density {_cruise.Density:0})", _cruise.Locked);
        await Button();
        Check("and the generator will not start", !_cruise.Charging && !_cruise.On);

        // A course for the gate, on COMMS
        for (int i = radio.Contacts.ToList().IndexOf(gate.Radio); i > 0; i--)
            await Key(display, DisplayKey.Down);
        await Key(display, DisplayKey.Select);
        await Key(display, DisplayKey.Down);
        await Key(display, DisplayKey.Down);
        await Key(display, DisplayKey.Select);
        Check("COMMS sets the course for the gate", await Until(() => ReferenceEquals(_cruise.Course, gate.Radio), 2));

        // Out of the station's reach, the nose a little to the side of the gate
        Vector3 aimAt = gate.GlobalPosition + new Vector3(1500, 0, 0);
        Vector3 way = (aimAt - station.GlobalPosition).Normalized();
        Point(station.GlobalPosition + way * 15000, aimAt, 0);
        await Seconds(0.5);
        Check($"out here it can (density {_cruise.Density:0.0})", !_cruise.Locked);
        _controls.Throttle = 1;
        await Button();
        Check("the button starts it charging", _cruise.Charging);
        Check("it comes on", await Until(() => _cruise.On, 6));
        float coreBefore = engine.CoreTemperature;

        // Its wake shows
        var signature = _ship.GetChildren().OfType<ShipSignature>().First();
        Check("it comes out of the hull's echo into the wake's", await Until(() => signature.RadarCrossSection > 150, 20));

        // The cockpit shakes, as much as the player likes, and only the eye
        var pilot = _ship.GetNode<Node3D>("Pilot");
        float most = 0;
        for (int i = 0; i < 120; i++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            most = Mathf.Max(most, pilot.Rotation.Length());
        }
        Check($"in cruise the cockpit shakes, a little ({Mathf.RadToDeg(most):0.00} degrees at most)", most > 0 && Mathf.RadToDeg(most) < 0.5f);
        Xpace.Menu.GameSettings settings = Xpace.Menu.GameSettings.Current;
        float shake = settings.CruiseShake;
        settings.CruiseShake = 0;
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        Check("and not at all with the setting at 0", pilot.Rotation == Vector3.Zero);
        settings.CruiseShake = shake;

        // Across the sector
        float top = 0, thinnest = 1, plate = 0;
        double run = 0, slowest = 0;
        float start = _ship.GlobalPosition.DistanceTo(gate.GlobalPosition);
        ulong longest = 0, all = 0;
        int over = 0;
        for (; run < 240 && _cruise.On; run += 1.0 / 60)
        {
            ulong before = Time.GetTicksUsec();
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            ulong took = Time.GetTicksUsec() - before;
            longest = Math.Max(longest, took);
            all += took;
            if (took > 16667)
                over++;
            float speed = _ship.LinearVelocity.Length();
            top = Mathf.Max(top, speed);
            thinnest = Mathf.Min(thinnest, _cruise.Thin);
            plate = Mathf.Max(plate, Plate(14) / 8f);
            if ((int)(run * 60) % 600 == 0)
                GD.Print($"     {run,4:0} s  {_ship.GlobalPosition.DistanceTo(gate.GlobalPosition) / 1000,6:0.0} km  {speed,5:0} m/s  " +
                         $"density {_cruise.Density,5:0.0}  veil x{_cruise.Thin:0.000}");
        }
        float there = _ship.GlobalPosition.DistanceTo(gate.GlobalPosition);
        float atDrop = _ship.LinearVelocity.Length();
        GD.Print($"     {start / 1000:0} km in {run:0} s, at most {top:0} m/s (the plate said {plate:0}); step {all / 1000.0 / Math.Max(run * 60, 1):0.0} ms on average, longest {longest / 1000.0:0.0}, {over} over 16.7");
        Check($"it crosses the sector in under three minutes ({run:0} s)", run < 180);
        Check($"at kilometres a second ({top:0} m/s, veil x{thinnest:0.000})", top > 2000 && top < 4096);
        Check($"the engine rested ({coreBefore:0} to {engine.CoreTemperature:0} C)", engine.CoreTemperature <= coreBefore + 1);
        Check($"it drops out by itself near the gate ({there / 1000:0.0} km, {atDrop:0} m/s)",
              _cruise.DroppedOut && there is > 2500 and < 7000 && atDrop < 1000);
        _controls.Throttle = 0;
        Check("and the course stays on it", ReferenceEquals(_cruise.Course, gate.Radio));
        Check("the normal veil takes it down to flying speed", await Until(() => _ship.LinearVelocity.Length() < 420, 15));
        await Seconds(1);
        radio.AskTransit();
        Check("from there its radio reaches the gate", await Until(() => gate.Cleared(_ship), 3));

        // Stopped at full speed: braked within the generator's limit
        Vector3 open = gate.GlobalPosition + (station.GlobalPosition - gate.GlobalPosition) * 0.5f;
        Point(open, open + new Vector3(1000, 0, 1000), 0);
        await Seconds(0.2);
        _controls.Throttle = 1;
        await Button();
        Check("it starts again in open space", await Until(() => _cruise.On, 6));
        Check("and gets going", await Until(() => _ship.LinearVelocity.Length() > 2000, 40));

        // The path follows the nose, at a cost in speed
        Vector3 from = _ship.LinearVelocity.Normalized();
        _ship.GlobalBasis = _ship.GlobalBasis.Rotated(Vector3.Up, Mathf.DegToRad(30));
        await Seconds(12);
        float off = Mathf.RadToDeg(_ship.LinearVelocity.AngleTo(-_ship.GlobalBasis.Z));
        Check($"its nose turned 30 degrees, the path follows ({off:0.0} degrees off after 12 s)", off < 6 && _ship.LinearVelocity.Normalized().Dot(from) < 0.95f);

        _controls.Throttle = 0;
        float hardest = 0;
        await Button();
        Check("the button stops it", !_cruise.On);
        float last = _ship.LinearVelocity.Length();
        for (int i = 0; i < 12 * 60; i++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            float now = _ship.LinearVelocity.Length();
            hardest = Mathf.Max(hardest, (last - now) * 60);
            last = now;
        }
        Check($"braked no harder than the generator's limit ({hardest:0} m/s2)", hardest < _cruise.Spec.BrakeAcceleration * 1.1f);
        Check($"down to flying speed in 12 s ({last:0} m/s)", last < 420);
        Check("the veil is as normal again", Mathf.IsEqualApprox(_cruise.Thin, 1));
        Check($"and the echo is the hull's ({signature.RadarCrossSection:0} m2)", signature.RadarCrossSection < 20);

        // Caught in an interdictor's beam
        var interdictor = _ship.Find<InterdictorDevice>();
        Check("nothing locked, the interdictor is off", !interdictor.On);
        await Press(() => _controls.Interdict = true, () => _controls.Interdict = false);
        Check("its button turns it on, through the fire control", interdictor.On && FireControl.Status(_ship) == "INTERDICT");
        await Press(() => _controls.Interdict = true, () => _controls.Interdict = false);
        Check("and off", !interdictor.On);
        var hunter = GD.Load<PackedScene>("res://scenes/ships/ai_fighter.tscn").Instantiate<ShipBody>();
        hunter.GetNode("Pilot").ProcessMode = ProcessModeEnum.Disabled;
        hunter.AddChild(new InterdictorDevice { Name = "Interdictor", Wander = 0, Quiver = 0, Centre = 0 });
        _main.Sector.AddChild(hunter);
        Point(open, open + new Vector3(1000, 0, 1000), 0);
        _controls.Throttle = 1;
        await Seconds(0.5);
        await Button();
        Check("cruising again", await Until(() => _cruise.On && _ship.LinearVelocity.Length() > 1500, 40));
        hunter.GlobalPosition = _ship.GlobalPosition + _ship.LinearVelocity.Normalized() * 40000;
        var beam = hunter.Find<InterdictorDevice>();
        beam.Forced = _ship;
        hunter.Computer.Bus.Out(InterdictorDevice.Slot, 5, 1);
        beam.AfterStep(hunter, 0);
        Check("the hunter's beam is on it", beam.On);
        Check($"pulled out of cruise by the beam alone", await Until(() => _cruise.DroppedOut, 30));
        float gap = hunter.GlobalPosition.DistanceTo(_ship.GlobalPosition);
        Check($"at about where its generator gives up ({gap / 1000:0.0} km)", gap is > 2000 and < 11000);
        beam.Forced = null;
        hunter.Computer.Bus.Out(InterdictorDevice.Slot, 5, 0);
        beam.AfterStep(hunter, 0);
        _controls.Throttle = 0;

        // Over the network
        var sent = new ShipSnapshot { Orientation = Quaternion.Identity, Velocity = new Vector3(3990, -2000, 100), Wake = 1 };
        ShipSnapshot got = ShipSnapshot.Read(new BitReader(sent.ToBytes()));
        Check("a cruising ship's speed fits a snapshot", got.Velocity.DistanceTo(sent.Velocity) < 0.2f);
        Check("so does its wake", got.Wake == 1);

        GD.Print(_failures == 0 ? "ALL PASSED" : $"{_failures} FAILED");
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GetTree().Quit(_failures == 0 ? 0 : 1);
    }

    private unsafe int Plate(int at)
    {
        byte* table = _ship.Computer.Memory + ShipMemory.Base + ShipMemory.Ratings;
        return table[at] | table[at + 1] << 8;
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

    private async Task Press(Action down, Action up)
    {
        down();
        await Seconds(0.2);
        up();
        await Seconds(0.3);
    }

    private async Task Button()
    {
        _controls.Cruise = true;
        await Seconds(0.1);
        _controls.Cruise = false;
        await Seconds(0.1);
    }

    private async Task Key(DisplayDevice display, DisplayKey key)
    {
        display.PressKeyOn("COMMS", key);
        await Seconds(0.3);
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
}
