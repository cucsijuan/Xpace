using System;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Xpace.Computer;
using Xpace.Ship;
using Xpace.Space;

namespace Xpace.Tests;

/// <summary>
/// Docking on a landing pad and launching from it, with the Synty station
/// (its first hangar, with the pads the hangar's scene has: two or more)
/// and a fighter running the stock diskette (FLIGHT, FIRECTL, COMMS):
///
///   - cleared to the hangar and one of its pads: only that pad glows red,
///     the radio hears its beacon and FLIGHT takes the RADAR screen to show
///     it; over another pad, lined up, low and still, nothing takes the ship;
///   - turned away from the pad's heading, low and still, the clamps do
///     not take the ship; lined up (within the leeway, off the middle) but
///     high, the glow is green and the clamps still wait;
///   - coming down with the controls, the clamps take it and bring it,
///     slowly, exactly to its place; docked, the thrusters move it no more,
///     the station shuts the door and says so;
///   - a second ship cleared to the same hangar gets another pad: the door
///     opens for it, its pad lights, and shuts once it is docked too;
///   - REQUEST LAUNCH on COMMS: the door opens, the clamps lift the ship and
///     let it go, and do not take it again; out of the hangar, the door
///     shuts and the clearance is over; FIRECTL has the RADAR screen back.
///
/// Run: Godot --headless --fixed-fps 60 --path . res://tests/landing_pad_test.tscn
/// Exits with 0 if every check passed.
/// </summary>
public partial class LandingPadTest : Node3D
{
    private ShipBody _ship;
    private RadioDevice _radio;
    private DisplayDevice _display;
    private PilotControls _controls;
    private int _failures;

    public override async void _Ready()
    {
        var station = GD.Load<PackedScene>("res://scenes/stations/synty_station.tscn").Instantiate<Node3D>();
        station.Name = "Haven";
        StationRadio radio = station.GetNode<StationRadio>("Radio");
        radio.ClearanceSeconds = 60;

        // The ship 600 m out in front of the first hangar's door
        AddChild(station);
        HangarDock dock = station.GetNode("HangarBig1").GetNode<HangarDock>("Dock");
        Check($"the hangar has its pads ({dock.Pads.Count}), numbered, each finding the dock",
              dock.Pads.Count >= 2 && dock.Pads.Select(p => p.Number).SequenceEqual(Enumerable.Range(1, dock.Pads.Count)) && dock.Pads.All(p => p.Dock == dock));

        _ship = GD.Load<PackedScene>("res://scenes/ships/ai_fighter.tscn").Instantiate<ShipBody>();
        _ship.GetNode("Pilot").Free();
        _display = _ship.GetNode<DisplayDevice>("Display");
        var screens = new Godot.Collections.Array<DisplayScreen>(_display.Screens) { GD.Load<DisplayScreen>("res://assets/ship/displays/comms.tres") };
        _display.Screens = screens;
        _radio = _ship.GetNode<RadioDevice>("Radio");
        _radio.Callsign = "TESTER";
        _ship.ControlsSource = () => _controls;
        AddChild(_ship);
        _ship.GlobalTransform = dock.GlobalTransform * new Transform3D(new Basis(Vector3.Up, 0), new Vector3(0, 0, 600));

        Check("COMMS.COM is resident", await Until(() => _ship.Computer.ScreenText().Contains("Radio resident"), 90));
        await Seconds(1);
        Check($"the fighter fits the pads (its box {_ship.Bounds.Size})", dock.Pads.All(p => p.Fits(_ship)));
        Check("no beacon yet: FIRECTL has the RADAR screen", Lent() == 0 && Pixel(0, 64) != 0);

        _radio.AskDocking();
        Check("cleared to the hangar and a pad in it", await Until(() => _radio.Dock == dock.Number && _radio.Pad != 0, 3));
        LandingPad pad = dock.PadNumbered(_radio.Pad);
        LandingPad other = dock.Pads.First(p => p != pad);
        await Seconds(0.5);
        Check($"pad {pad.Number} glows red ({pad.State}), only it", pad.State == LandingPad.PadState.Waiting && GlowShown(pad) &&
              dock.Pads.Where(p => p != pad).All(p => p.State == LandingPad.PadState.Idle && !GlowShown(p)));
        Check($"COMMS says which\n{Comms()}", Comms().Contains($"HANGAR {dock.Number} PAD {pad.Number}"));
        Check($"the radio hears the beacon (flags {Guide(0)})", (Guide(0) & RadioDevice.PadGuidance) != 0);
        Check("FLIGHT takes the RADAR screen for it", await Until(() => Lent() == 'F' && Pixel(0, 64) == 0, 1));
        Check("the door opens", await Until(() => dock.Door.At >= dock.PassableAt, 15));

        // Over another pad, lined up, low and still: not taken
        Place(other.GlobalTransform * other.Rest(_ship).TranslatedLocal(new Vector3(0, 2, 0)), 0);
        await Seconds(1.5);
        Check($"over pad {other.Number}, not its own: nothing takes it ({other.State})", !_ship.Held && other.State == LandingPad.PadState.Idle);

        // Over the pad, 4 m above the ship's place, still, but turned 30 degrees: not taken
        Transform3D rest = pad.GlobalTransform * pad.Rest(_ship);
        Place(rest.Rotated(Vector3.Up, 0).TranslatedLocal(new Vector3(0, 4, 0)), Mathf.DegToRad(30));
        await Seconds(1.5);
        Shot("1_turned");
        Check($"turned 30 degrees away, low and still: red, not taken ({pad.State})", pad.State == LandingPad.PadState.Waiting && !_ship.Held);
        float yaw = Guide(8, word: true) * 360f / 65536f;
        Check($"the beacon says to yaw {yaw:0.0} degrees", Mathf.Abs(yaw + 30) < 2);

        // Lined up, 2 m off the middle, 12 m above its place (clear of the ceiling): green, but too high for the clamps
        Place(rest.TranslatedLocal(new Vector3(2, 12, 0)), 0);
        await Seconds(1);
        Shot("2_lined_up");
        Check($"lined up but high: green, not taken ({pad.State})", pad.State == LandingPad.PadState.Aligned && !_ship.Held);
        Check($"the beacon says so (flags {Guide(0)}; up {Guide(4, word: true) / 16f:0.0} m)",
              (Guide(0) & RadioDevice.PadAligned) != 0 && Mathf.Abs(Guide(4, word: true) / 16f + 12) < 0.5f);

        // Down, with the controls
        _controls = new PilotControls { Up = -0.03f };
        float speed = 0;
        Check("coming down, the clamps take it", await Until(() =>
        {
            if (!_ship.Held)
                speed = _ship.LinearVelocity.Length();
            return _ship.Held;
        }, 30));
        _controls = default;
        Shot("3_taken");
        Check($"they took it moving slowly ({speed:0.00} m/s)", speed <= pad.CaptureSpeed);
        Check("and bring it to its place", await Until(() => pad.State == LandingPad.PadState.Docked, (float)pad.SettleSeconds + 1));
        await Seconds(0.1);
        float off = _ship.GlobalPosition.DistanceTo(rest.Origin);
        float turned = _ship.GlobalBasis.Z.AngleTo(rest.Basis.Z);
        Check($"exactly ({off:0.000} m, {Mathf.RadToDeg(turned):0.00} degrees off)", off < 0.01f && turned < 0.002f);
        Shot("4_docked");
        Check($"the beacon says docked (flags {Guide(0)})", (Guide(0) & (RadioDevice.PadHeld | RadioDevice.PadDocked)) == (RadioDevice.PadHeld | RadioDevice.PadDocked));
        Check("the station shuts the door and says so", await Until(() => !dock.IsOpen && dock.Door.Open == 0, 2) && Heard("Docked"));
        _controls = new PilotControls { Throttle = 1, Up = 1, Yaw = 1 };
        await Seconds(1);
        _controls = default;
        off = _ship.GlobalPosition.DistanceTo(rest.Origin);
        Check($"docked, the thrusters move it no more ({off:0.000} m)", off < 0.01f && pad.State == LandingPad.PadState.Docked);
        Check("still docked a minute later (the clearance holds while docked)", await Stays(() => _radio.Dock == dock.Number && pad.State == LandingPad.PadState.Docked, 65));

        // A second ship for the same hangar: the other pad
        var second = new ShipBody { Name = "Second", Spec = _ship.Spec };
        second.AddChild(new ShipComputer { Name = "ShipComputer", PowerOnAtStart = false });
        second.AddChild(GD.Load<PackedScene>("res://scenes/ships/fighter_03_hitboxes.tscn").Instantiate());
        var secondRadio = new RadioDevice { Name = "Radio", Callsign = "SECOND" };
        second.AddChild(secondRadio);
        AddChild(second);
        second.GlobalTransform = dock.GlobalTransform * new Transform3D(Basis.Identity, new Vector3(0, 0, 300));
        await Seconds(0.5);
        secondRadio.AskDocking();
        Check("a second ship is cleared to the same hangar, the other pad", await Until(() => secondRadio.Dock == dock.Number && secondRadio.Pad != 0, 3)
              && secondRadio.Pad != _radio.Pad);
        LandingPad secondPad = dock.PadNumbered(secondRadio.Pad);
        Check($"the door opens for it, its pad lights ({secondPad?.State})", await Until(() => dock.IsOpen && secondPad.State == LandingPad.PadState.Waiting, 2)
              && pad.State == LandingPad.PadState.Docked);
        Transform3D secondRest = secondPad.GlobalTransform * secondPad.Rest(second);
        second.State = new ShipState { Position = secondRest.TranslatedLocal(new Vector3(0, 3, 0)).Origin, Orientation = secondRest.Basis.GetRotationQuaternion() };
        Check("it docks there", await Until(() => secondPad.State == LandingPad.PadState.Docked, (float)secondPad.SettleSeconds + 2));
        Check("both docked: the door shuts", await Until(() => !dock.IsOpen, 2));

        // REQUEST LAUNCH on COMMS: the station is the first contact
        await Key(DisplayKey.Select);
        Check($"COMMS offers a launch\n{Comms()}", Comms().Contains("REQUEST LAUNCH"));
        await Key(DisplayKey.Down);
        await Key(DisplayKey.Down);
        await Key(DisplayKey.Select);
        Check("the station opens the door", await Until(() => dock.IsOpen, 2) && Heard("launch"));
        Check($"the clamps lift the ship ({pad.State})", await Until(() => pad.State == LandingPad.PadState.Launching, 1));
        Check("and let it go", await Until(() => !_ship.Held, (float)pad.LaunchSeconds + 1));
        float height = (pad.GlobalTransform.AffineInverse() * _ship.GlobalPosition).Y - pad.Rest(_ship).Origin.Y;
        Check($"{height:0.0} m above its place", Mathf.Abs(height - pad.LaunchHeight) < 0.3f);
        Check("hovering, it is not taken again", await Stays(() => !_ship.Held, 3));

        // Out of the hangar: the door shuts behind it, the clearance is over
        Place(dock.GlobalTransform * new Transform3D(Basis.Identity, new Vector3(0, 0, 200)), 0);
        Shot("5_out");
        Check("out of the hangar: the door shuts, the clearance is over", await Until(() => _radio.Dock == 0 && !dock.IsOpen, 2) && Heard("Fly safe"));
        Check("the second ship stays docked", secondPad.State == LandingPad.PadState.Docked && secondRadio.Dock == dock.Number);
        await Seconds(0.5);
        Check($"the pad is dark ({pad.State})", pad.State == LandingPad.PadState.Idle && !GlowShown(pad));
        Check("FIRECTL has the RADAR screen back", await Until(() => Lent() == 0 && Pixel(0, 64) != 0, 1));
        Shot("6_back");

        GD.Print(_failures == 0 ? "ALL PASSED" : $"{_failures} FAILED");
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GetTree().Quit(_failures == 0 ? 0 : 1);
    }

    // The ship at the place, turned about its up by the angle (right positive), still
    private void Place(Transform3D at, float yawRight)
    {
        Basis basis = at.Basis * new Basis(Vector3.Up, -yawRight);
        _ship.State = new ShipState { Position = at.Origin, Orientation = basis.GetRotationQuaternion() };
    }

    private static bool GlowShown(LandingPad pad) => pad.GetNode<Node3D>("Glow/Column").Visible;

    private bool Heard(string words) => _radio.Heard.Any(m => m.Text.Contains(words));

    private unsafe int Guide(int offset, bool word = false)
    {
        byte* p = _ship.Computer.Memory + ShipMemory.Base + ShipMemory.Pad + offset;
        return word ? (short)(p[0] | p[1] << 8) : p[0];
    }

    private unsafe byte* Radar(int offset)
    {
        int index = _display.IndexOf("RADAR");
        return _ship.Computer.Memory + ShipMemory.Base + ShipMemory.Screens + index * DisplayDevice.EntrySize + offset;
    }

    private unsafe int Lent() => *Radar(9);

    // A pixel of the RADAR screen (0 to 3)
    private unsafe int Pixel(int x, int y)
    {
        ushort memory = *(ushort*)Radar(14);
        int width = *(ushort*)Radar(10);
        byte b = _ship.Computer.Memory[ShipMemory.Base + memory + y * width / 4 + x / 4];
        return (b >> (6 - 2 * (x & 3))) & 3;
    }

    private string Comms() => _display.Text("COMMS");

    // XPACE_PAD_SHOTS=dir: the RADAR screen as the pilot sees it, at the test's steps
    private void Shot(string name)
    {
        string dir = System.Environment.GetEnvironmentVariable("XPACE_PAD_SHOTS");
        if (string.IsNullOrEmpty(dir))
            return;
        var image = Image.CreateEmpty(160, 128, false, Image.Format.L8);
        for (int y = 0; y < 128; y++)
            for (int x = 0; x < 160; x++)
                image.SetPixel(x, y, new Color(Pixel(x, y) / 3f, 0, 0));
        image.Resize(640, 512, Image.Interpolation.Nearest);
        image.SavePng($"{dir}/{name}.png");
    }

    private async Task Key(DisplayKey key)
    {
        _display.PressKeyOn("COMMS", key);
        await Seconds(0.3);
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
        return done();
    }

    private async Task<bool> Stays(Func<bool> holds, double seconds)
    {
        for (int i = 0; i < seconds * Engine.PhysicsTicksPerSecond; i++)
        {
            if (!holds())
                return false;
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        }
        return true;
    }

    private void Check(string what, bool ok)
    {
        GD.Print((ok ? "ok   " : "FAIL ") + what);
        if (!ok)
            _failures++;
    }
}
