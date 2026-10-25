using System;
using System.Threading.Tasks;
using Godot;
using Xpace.Computer;
using Xpace.Net;
using Xpace.Ship;
using Xpace.Space;

namespace Xpace.Tests;

/// <summary>
/// The jammer, on a ship (the prey) whose computer runs RWR.COM, against
/// another (the hunter) whose computer runs FIRECTL:
///
///   - RWR.COM steps the jammer on the pilot's button (off, noise,
///     deception, off) and aims it at the radar that locks the ship;
///   - noise: far out the hunter's radar cannot see the prey, only a strobe
///     at its bearing, which FIRECTL does not lock; close in it burns
///     through; aimed at the hunter it hides the prey nearer than all
///     round, aimed away it does nothing; with its capacitor dry it is
///     quiet; a warning receiver hears it as a 'J';
///   - a simple radar (a drone's) sees the prey only nearer;
///   - deception pulls the hunter's track away and breaks the lock far
///     out; close in the skin echo wins; a model without deception makes
///     noise when asked to deceive;
///   - the jammer's state goes through a snapshot and its delta.
///
/// Run: Godot --headless --fixed-fps 60 --path . res://tests/jammer_test.tscn
/// Exits with 0 if every check passed.
/// </summary>
public partial class JammerTest : Node3D
{
    private ShipBody _hunter, _prey;
    private RadarDevice _radar;
    private RwrDevice _hunterRwr;
    private DisplayDevice _hunterDisplay;
    private JammerDevice _jammer;
    private PowerDevice _power;
    private ShipSignature _signature;
    private Node3D _podNode;
    private ShipSignature _pod;
    private JammerDevice _jam;
    private PilotControls _hunterControls, _preyControls;
    private int _failures;

    public override async void _Ready()
    {
        AddChild(new Projectiles());

        _hunter = new ShipBody { Name = "Hunter" };
        _hunter.AddChild(new ShipComputer { Name = "ShipComputer" });
        _hunter.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(5, 3, 10) } });
        _hunterDisplay = new DisplayDevice { Screens = Screens(false) };
        _hunter.AddChild(_hunterDisplay);
        _hunter.AddChild(new TurretDevice { Name = "Turret", Position = new Vector3(0.9f, 0.8f, -3f) });
        _radar = new RadarDevice { Name = "Radar", Position = new Vector3(0, 0.5f, -5f), TrackOriginPath = "../Turret", EmitterPath = "Emitter" };
        _radar.AddChild(new RadarEmitter { Name = "Emitter", Signature = "F", PowerAt1Km = 4e4f });
        _hunter.AddChild(_radar);
        _hunterRwr = new RwrDevice { Name = "Rwr" };
        _hunter.AddChild(_hunterRwr);
        _hunter.AddChild(new ShipSignature { Name = "Signature" });
        _hunter.ControlsSource = () => _hunterControls;
        AddChild(_hunter);

        _prey = new ShipBody { Name = "Prey" };
        _prey.AddChild(new ShipComputer { Name = "ShipComputer" });
        _prey.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(5, 3, 10) } });
        _prey.AddChild(new DisplayDevice { Screens = Screens(true) });
        _prey.AddChild(new RwrDevice { Name = "Rwr" });
        _power = new PowerDevice { Name = "Power" };
        _prey.AddChild(_power);
        _prey.AddChild(new HeatDevice { Name = "Heat" });
        _jammer = new JammerDevice { Name = "Jammer", Spec = GD.Load<JammerSpec>("res://assets/ship/jammers/jammer_dj3.tres") };
        _prey.AddChild(_jammer);
        _signature = new ShipSignature { Name = "Signature" };
        _prey.AddChild(_signature);
        _prey.ControlsSource = () => _preyControls;
        AddChild(_prey);
        Place(4000);

        _podNode = new Node3D { Name = "Pod" };
        _pod = new ShipSignature { Name = "Signature" };
        _jam = new JammerDevice { Name = "Jammer", Spec = GD.Load<JammerSpec>("res://assets/ship/jammers/jammer_dj3.tres") };
        _podNode.AddChild(_pod);
        _podNode.AddChild(_jam);
        AddChild(_podNode);
        Park(_podNode);

        Check("FIRECTL runs on the hunter", await Until(_hunter, () => _hunterDisplay.Text("WEAPON").Contains("FIRECTL"), 40));
        Check("RWR.COM runs on the prey", await Until(_prey, () => _prey.Computer.ScreenText().Contains("Threat warning resident"), 40));

        Snapshots();
        await Buttons();
        await Noise();
        await Antenna();
        await DroneRadar();
        await Deception();

        GD.Print(_failures == 0 ? "ALL PASSED" : $"{_failures} FAILED");
        GetTree().Quit(_failures == 0 ? 0 : 1);
    }

    // ---------- the software ----------

    private async Task Buttons()
    {
        _jammer.Set(JamMode.Off);
        await PressJammer();
        Check($"the jammer button: noise ({_jammer.Mode})", _jammer.Mode == JamMode.Noise);
        await PressJammer();
        Check($"again: deception, the model has it ({_jammer.Mode})", _jammer.Mode == JamMode.Deception);
        await PressJammer();
        Check($"again: off ({_jammer.Mode})", _jammer.Mode == JamMode.Off);

        // RWR.COM aims the noise at the radar that locks the ship, and the lock breaks
        Place(4000);
        await Seconds(1.2);
        await Lock();
        Check($"4 km, the jammer off: the hunter locks the prey (SNR {Db(_radar.Snr(_signature))} dB)", _radar.Locked == _signature);
        await PressJammer();
        await Seconds(0.5);
        Vector3 toHunter = _prey.GlobalBasis.Inverse() * (_hunter.GlobalPosition - _prey.GlobalPosition);
        float off = Mathf.RadToDeg(_jammer.Aim.AngleTo(toHunter with { Y = 0 }));
        Check($"RWR.COM aims the beam at the radar locking the ship ({off:0.0} degrees off)", _jammer.Aimed && off < 15);
        Check($"and the lock breaks (SNR {Db(_radar.Snr(_signature))} dB)", _radar.Locked == null);
        Check("the hunter's warning receiver hears a J", System.Linq.Enumerable.Any(_hunterRwr.Contacts, c => c.Letter == 'J'));

        float heat = _prey.Device<HeatDevice>().Level;
        _power.Set(0, 6, 6);
        _power.Charge[(int)PowerStore.Systems] = 0.5;
        await Seconds(1.5);
        Check($"no power to systems: the capacitor runs dry and the jammer goes quiet ({_jammer.Powered:0.00})", _jammer.Powered < 0.01f);
        await Seconds(0.5);
        Check("and the hunter takes the prey back", _radar.Locked == _signature);
        _power.Set(4, 4, 4);
        _power.Rearm();
        _jammer.Set(JamMode.Off);
        await LetGo();
        await Seconds(4);                       // past the radar's memory of it
        await LetGo();
        await Seconds(1);
        Check("with no lock on the ship RWR.COM sends all round", !_jammer.Aimed);

        _jammer.Set(JamMode.Noise);
        await Seconds(10);
        float jamming = _prey.Device<HeatDevice>().Level;
        _jammer.Set(JamMode.Off);
        await Seconds(10);
        float quiet = _prey.Device<HeatDevice>().Level;
        Check($"its power ends as heat: {jamming * 100:0.0}% after 10 s sending, {quiet * 100:0.0}% after 10 s off", jamming > quiet + 0.01f);
        Park(_prey);
    }

    // ---------- noise (on a pod: a signature with a jammer, no software) ----------

    private async Task Noise()
    {
        _jam.AllRound();
        PlacePod(4000);
        await Seconds(1.2);
        await Lock();
        Check($"4 km, the jammer off: the hunter locks the pod (SNR {Db(_radar.Snr(_pod))} dB)", _radar.Locked == _pod);
        await LetGo();

        _jam.Set(JamMode.Noise);
        await Seconds(1.2);
        Check($"4 km, noise all round: the pod is not seen (SNR {Db(_radar.Snr(_pod))} dB)", !InTable(out _));
        bool strobe = Strobe(out float bearing);
        Check($"a strobe at its bearing, with no range ({bearing:0.0} degrees off)", strobe && MathF.Abs(bearing) < 3);
        await Lock();
        Check("FIRECTL does not lock the strobe", _radar.Locked == null);

        PlacePod(1000);
        await Seconds(1.2);
        await Lock();
        Check($"1 km, noise all round: the radar burns through (SNR {Db(_radar.Snr(_pod))} dB)", _radar.Locked == _pod);
        await LetGo();
    }

    private async Task Antenna()
    {
        PlacePod(3000);
        _jam.Set(JamMode.Noise);
        _jam.AllRound();
        await Seconds(1.2);
        await Lock();
        Check($"3 km, noise all round: the radar still locks (SNR {Db(_radar.Snr(_pod))} dB)", _radar.Locked == _pod);
        await LetGo();
        _jam.Point(0, 0);                       // the pod faces the hunter: ahead is at it
        await Seconds(1.2);
        await Lock();
        Check($"aimed at the hunter: no lock (SNR {Db(_radar.Snr(_pod))} dB)", _radar.Locked == null);
        _jam.Point(Mathf.Pi, 0);
        await Seconds(1.2);
        await Lock();
        Check($"aimed away: it locks (SNR {Db(_radar.Snr(_pod))} dB)", _radar.Locked == _pod);
        await LetGo();
        PlacePod(1000);
        _jam.Point(0, 0);
        await Seconds(1.2);
        await Lock();
        Check($"1 km, aimed at it: it burns through (SNR {Db(_radar.Snr(_pod))} dB)", _radar.Locked == _pod);
        await LetGo();
        _jam.Set(JamMode.Off);
        _jam.AllRound();
    }

    /// <summary>A drone's radar (no tracker): it sees the pod only nearer.</summary>
    private async Task DroneRadar()
    {
        PlacePod(3000);
        var host = new Node3D { Name = "DroneRadar", Position = _hunter.GlobalPosition + new Vector3(30, 0, 0) };
        AddChild(host);
        host.LookAt(_podNode.GlobalPosition, Vector3.Up);
        var emitter = new RadarEmitter { LockRange = 4000, DetectRange = 4000, Target = _pod };
        host.AddChild(emitter);
        await Seconds(4.5);
        Check("a drone's radar locks the pod at 3 km, the jammer off", emitter.Locked);
        _jam.Set(JamMode.Noise);
        await Seconds(1);
        bool lost = !emitter.Locked;
        await Seconds(4);
        Check("with noise all round it loses it and cannot lock again", lost && !emitter.Locked);
        PlacePod(1000);
        host.LookAt(_podNode.GlobalPosition, Vector3.Up);
        await Seconds(4.5);
        Check("at 1 km it burns through", emitter.Locked);
        _jam.Set(JamMode.Off);
        host.QueueFree();
        await Seconds(0.1);
    }

    // ---------- deception ----------

    private async Task Deception()
    {
        PlacePod(4000);
        await Seconds(1.2);
        await Lock();
        Check("4 km: the hunter locks the pod", _radar.Locked == _pod);
        _jam.Point(0, 0);
        _jam.Set(JamMode.Deception);
        float worst = 0;
        bool lost = false, onCopy = false;
        for (int i = 0; i < 12 * 60 && !lost; i++)
        {
            await Seconds(1.0 / 60);
            onCopy |= _radar.Locked is FalseEcho;
            if (_radar.Locked == null)
                lost = true;
            else
                worst = MathF.Max(worst, _radar.TrackPosition.DistanceTo(_podNode.GlobalPosition));
        }
        Check($"deception: the tracker takes the copy, the track runs {worst:0} m off", onCopy && worst > 800);
        Check("and the lock breaks when the copy stops; the radar does not find the pod where it lost it", lost);
        await Seconds(1);
        Check("it stays lost", _radar.Locked == null);
        _jam.Set(JamMode.Off);
        await Seconds(0.5);

        PlacePod(1000);
        await Seconds(1.2);
        await Lock();
        _jam.Set(JamMode.Deception);
        worst = 0;
        for (int i = 0; i < 10 * 60; i++)
        {
            await Seconds(1.0 / 60);
            if (_radar.Locked != null)
                worst = MathF.Max(worst, _radar.TrackPosition.DistanceTo(_podNode.GlobalPosition));
        }
        Check($"1 km: the skin echo is stronger than the copy, the track stays on (at most {worst:0} m off)",
              _radar.RealLocked == _pod && worst < 150);
        _jam.Set(JamMode.Off);
        await LetGo();

        var simple = new JammerDevice { Name = "Simple", Spec = GD.Load<JammerSpec>("res://assets/ship/jammers/jammer_nj1.tres") };
        AddChild(simple);
        simple.Set(JamMode.Deception);
        Check($"a model without deception makes noise when asked to deceive ({simple.Mode})", simple.Mode == JamMode.Noise);
        simple.QueueFree();
    }

    // ---------- the network ----------

    private void Snapshots()
    {
        var a = new ShipSnapshot
        {
            Tick = 100, Orientation = Quaternion.Identity, Jam = JamMode.Deception, JamModel = 2, JamAimed = true,
            JamAim = new Vector3(0.5f, 0.2f, -1).Normalized(),
        };
        ShipSnapshot b = ShipSnapshot.Read(new BitReader(a.ToBytes()));
        Check($"a snapshot carries the jammer: {b.Jam}, model {b.JamModel}, aimed {b.JamAimed}, {Mathf.RadToDeg(b.JamAim.AngleTo(a.JamAim)):0.0} degrees off",
              b.Jam == JamMode.Deception && b.JamModel == 2 && b.JamAimed && b.JamAim.AngleTo(a.JamAim) < Mathf.DegToRad(2));
        Check($"{a.ToBytes().Length} bytes, at most {ShipSnapshot.MaxBytes}", a.ToBytes().Length <= ShipSnapshot.MaxBytes);
        var quiet = new ShipSnapshot { Tick = 100, Orientation = Quaternion.Identity };
        Check($"with the jammer off it is still {ShipSnapshot.Bytes} bytes ({quiet.ToBytes().Length})", quiet.ToBytes().Length == ShipSnapshot.Bytes);

        ShipSnapshot.Wire baseWire = quiet.Encode();
        a.Tick = 103;
        var w = new BitWriter();
        a.Encode().WriteDelta(w, baseWire);
        var r = new BitReader(w.ToArray());
        (uint tick, _) = ShipSnapshot.Wire.ReadDeltaHeader(r, 103);
        ShipSnapshot d = ShipSnapshot.Wire.ReadDelta(r, tick, baseWire).Decode();
        Check($"a delta carries the jammer switched on ({d.Jam}, aimed {d.JamAimed})", d.Jam == JamMode.Deception && d.JamAimed);
    }

    // ---------- helpers ----------

    /// <summary>Out of the way: behind the hunter, outside its radar's cone.</summary>
    private void Park(Node3D what)
    {
        if (what is ShipBody ship)
            ship.State = new ShipState { Position = new Vector3(0, 0, 8000), Orientation = Quaternion.Identity };
        else
            what.GlobalPosition = new Vector3(0, 0, 9000);
    }

    /// <summary>The pod ahead of the hunter at a range, a little above, facing it.</summary>
    private void PlacePod(float range)
    {
        _hunter.State = new ShipState { Orientation = Quaternion.Identity };
        _podNode.GlobalTransform = new Transform3D(new Basis(Vector3.Up, Mathf.Pi), new Vector3(0, 0.05f * range, -range));
    }

    /// <summary>The prey ahead of the hunter at a range, a little above, facing it, still.</summary>
    private void Place(float range)
    {
        _hunter.State = new ShipState { Orientation = Quaternion.Identity };
        _prey.State = new ShipState
        {
            Position = new Vector3(0, 0.05f * range, -range),
            Orientation = new Quaternion(Vector3.Up, Mathf.Pi),
        };
    }

    private unsafe bool InTable(out int index)
    {
        byte* table = _hunter.Computer.Memory + ShipMemory.Base + ShipMemory.Contacts;
        int n = _hunter.Computer.Bus[RadarDevice.Slot, 3];
        for (index = 0; index < n; index++)
        {
            byte* e = table + index * 16;
            if ((e[0] | e[1] << 8) == _pod.RadarId && (e[10] & 2) == 0)
                return true;
        }
        return false;
    }

    private unsafe bool Strobe(out float bearing)
    {
        byte* table = _hunter.Computer.Memory + ShipMemory.Base + ShipMemory.Contacts;
        int n = _hunter.Computer.Bus[RadarDevice.Slot, 3];
        for (int i = 0; i < n; i++)
        {
            byte* e = table + i * 16;
            if ((e[10] & 2) != 0 && (e[2] | e[3] << 8) == 0)
            {
                bearing = (short)(e[4] | e[5] << 8) / 65536f * 360;
                return true;
            }
        }
        bearing = float.NaN;
        return false;
    }

    private async Task PressJammer()
    {
        _preyControls.Jammer = true;
        await Seconds(0.1);
        _preyControls.Jammer = false;
        await Seconds(0.1);
    }

    private async Task Lock()
    {
        if (_radar.Locked != null)
            await LetGo();
        _hunterControls.TargetAhead = true;
        await Seconds(0.1);
        _hunterControls.TargetAhead = false;
        await Seconds(0.2);
    }

    private async Task LetGo()
    {
        if (_radar.Locked == null)
            return;
        _hunterControls.TargetAhead = true;   // a press with a target locked lets it go
        await Seconds(0.1);
        _hunterControls.TargetAhead = false;
        await Seconds(0.1);
    }

    private static int Db(float snr) => (int)MathF.Round(10 * MathF.Log10(MathF.Max(snr, 1e-3f)));

    private static Godot.Collections.Array<DisplayScreen> Screens(bool rwr)
    {
        var screens = new Godot.Collections.Array<DisplayScreen>
        {
            GD.Load<DisplayScreen>("res://assets/ship/displays/flight.tres"),
            GD.Load<DisplayScreen>("res://assets/ship/displays/radar.tres"),
            GD.Load<DisplayScreen>("res://assets/ship/displays/weapon.tres"),
        };
        if (rwr)
            screens.Add(GD.Load<DisplayScreen>("res://assets/ship/displays/rwr.tres"));
        return screens;
    }

    private async Task Seconds(double seconds)
    {
        for (int i = 0; i < Math.Max(1, (int)Math.Round(seconds * Engine.PhysicsTicksPerSecond)); i++)
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
    }

    private async Task<bool> Until(ShipBody ship, Func<bool> done, double seconds)
    {
        for (int i = 0; i < seconds * Engine.PhysicsTicksPerSecond; i++)
        {
            if (done())
                return true;
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        }
        GD.Print(ship.Computer.ScreenText());
        return false;
    }

    private void Check(string what, bool ok)
    {
        GD.Print((ok ? "ok   " : "FAIL ") + what);
        if (!ok)
            _failures++;
    }
}
