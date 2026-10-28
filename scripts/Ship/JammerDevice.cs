using System;
using System.Collections.Generic;
using Godot;
using Xpace.Computer;
using Xpace.Net;
using Xpace.Space;

namespace Xpace.Ship;

/// <summary>
/// The radar jammer, bus slot 14 (1E0h). What it can do is its model's
/// (<see cref="Spec"/>, a <see cref="JammerSpec"/>).
///
/// Noise: it fills the radars' receivers with noise, so an echo has to be
/// stronger to be seen, and what is seen is measured worse. The noise falls
/// with the square of the distance and the echo with the fourth power, so
/// close in a radar burns through. A radar hears the noise from the
/// jammer's direction: a strobe on its scope, a bearing with no range.
///
/// Deception (models that have it): it hears a radar tracking the ship and
/// sends its pulses back, stronger than the skin echo: first on top of it,
/// so the tracker takes the copy for the target; then later and later, so
/// the copy runs off in range (its Doppler with it) and the gate goes with
/// it; then it stops, and the gate is left on nothing. A missile on that
/// radar's datalink is sent where the gate is. A copy weaker than the skin
/// echo (a radar close in) takes nothing.
///
/// The antenna sends all round, or, aimed, puts <see cref="JammerSpec.BeamGain"/>
/// times more into a beam toward a bearing the program gives (the radar
/// that has the ship locked, from the warning receiver).
///
/// Sending, it draws from the systems capacitor (with the shields); short
/// of power it sends as much as it gets. All it draws ends as heat. And it
/// is heard: warning receivers far beyond the range it hides the ship at
/// show it as a 'J'.
///
/// Registers: +0 type 16  +1 version 1  +2 mode (write: 0 off, 1 noise,
/// 2 deception; reads what it does: a model without deception makes noise)
/// +3 what the model can do (bit 0 noise, bit 1 deception)
/// +4 azimuth  +6 elevation of the aim (binary angles, 65536 to the turn,
/// ship axes, right and up positive)  +8 write: 0 all round, 1 aimed (takes
/// +4 and +6)  +9 status (bit 0 sending, bit 1 short of power, bit 2 a
/// tracking radar is being deceived)  +10 power it gets, 0 to 255
/// </summary>
public partial class JammerDevice : Node3D, IShipDevice, IDeceiver, IRadarEmitter, INetJammer
{
    public const int Slot = 14;
    public const byte Type = 16;

    [Export] public JammerSpec Spec { get; set; }

    public JamMode Mode { get; private set; }
    public bool Aimed { get; private set; }
    /// <summary>Where the aimed beam points, ship axes (Godot's: -Z ahead).</summary>
    public Vector3 Aim { get; private set; } = Vector3.Forward;
    /// <summary>The share of the power it wants that it gets, 0 to 1.</summary>
    public float Powered { get; private set; } = 1;
    public IRadarTarget Carrier { get; private set; }
    public ushort EmitterId { get; private set; }

    /// <summary>A tracking radar took a copy within the last moment.</summary>
    public bool Deceiving => Engine.GetPhysicsFrames() - _repeated <= 15;

    private JammerSpec S => Spec ??= new JammerSpec();
    private ShipBody _ship;
    private PowerDevice _power;
    private HeatDevice _heat;
    private ulong _repeated = ulong.MaxValue / 2;

    /// <summary>A pull on one radar's gate.</summary>
    private sealed class Pull
    {
        public double T;
        public ulong Frame;
    }

    private readonly Dictionary<object, Pull> _pulls = new();

    public override void _Ready()
    {
        EmitterId = RadarIds.Next();
        AddToGroup("jammers");
        AddToGroup("radar_emitters");
        // The echo it rides on: its parent, or one beside it (a ship's signature), or further up
        foreach (Node c in GetParent().GetChildren())
            Carrier ??= c as IRadarTarget;
        for (Node n = GetParent(); n != null && (Carrier == null || _ship == null); n = n.GetParent())
        {
            if (n is IRadarTarget t && Carrier == null)
                Carrier = t;
            if (n is ShipBody ship)
            {
                _ship = ship;
                foreach (Node c in ship.GetChildren())
                {
                    Carrier ??= c as IRadarTarget;
                    _power ??= c as PowerDevice;
                    _heat ??= c as HeatDevice;
                }
                break;
            }
        }
        if (GetParent() is IRadarTarget parent)
            Carrier = parent;
    }

    private bool Sending => Mode != JamMode.Off && Powered > 0 && (Carrier?.Alive ?? true) && !(_ship?.Wrecked ?? false);

    /// <summary>The antenna's gain toward a point: 1 all round; aimed, the beam's gain inside it and nothing outside.</summary>
    private float Gain(Vector3 point)
    {
        if (!Aimed || S.BeamGain <= 1)
            return 1;
        Vector3 to = point - GlobalPosition;
        Vector3 aim = (_ship?.GlobalBasis ?? GlobalBasis) * Aim;
        return to.LengthSquared() > 1 && aim.AngleTo(to) <= Mathf.DegToRad(S.BeamDegrees) / 2 ? S.BeamGain : 0;
    }

    public float NoiseToward(Vector3 point) =>
        Sending && Mode == JamMode.Noise ? S.NoiseAt1Km * Gain(point) * Powered : 0;

    public bool FalseEcho(object radar, Vector3 radarPosition, double delta, out Vector3 position, out Vector3 velocity, out float snrAt1Km)
    {
        position = velocity = Vector3.Zero;
        snrAt1Km = 0;
        if (!Sending || Mode != JamMode.Deception || Carrier == null)
            return false;
        float gain = Gain(radarPosition);
        if (gain <= 0)
            return false;
        ulong frame = Engine.GetPhysicsFrames();
        if (!_pulls.TryGetValue(radar, out Pull pull) || frame - pull.Frame > 1)
            _pulls[radar] = pull = new Pull();          // a new lock (or one taken again): capture first
        if (pull.Frame != frame)
            pull.T += delta;
        pull.Frame = frame;

        double walk = Math.Sqrt(2 * S.MaxPull / Math.Max(S.PullAcceleration, 1e-3));
        double t = pull.T;
        float off = 0, rate = 0;
        if (t >= S.CaptureSeconds + walk + S.DropSeconds)
            pull.T = t = 0;
        if (t >= S.CaptureSeconds + walk)
            return false;                               // dropped: the gate is left on nothing
        if (t > S.CaptureSeconds)
        {
            float w = (float)(t - S.CaptureSeconds);
            off = 0.5f * S.PullAcceleration * w * w;
            rate = S.PullAcceleration * w;
        }
        Vector3 away = (Carrier.GlobalPosition - radarPosition).Normalized();
        position = Carrier.GlobalPosition + away * off;
        velocity = Carrier.Velocity + away * rate;
        snrAt1Km = S.RepeaterAt1Km * gain * Powered;
        _repeated = frame;
        return true;
    }

    // ---------- what warning receivers hear ----------

    public string Signature => S.Signature;
    public float PowerAt1Km => S.HeardAt1Km * (Aimed ? MathF.Max(S.BeamGain, 1) : 1) * Powered;
    public bool Tracking => false;
    public bool Guiding => false;
    public float PrfHz => S.ToneHz;

    public bool Illuminates(Vector3 point)
    {
        if (!Sending || (Mode == JamMode.Deception && !Deceiving))
            return false;                               // a repeater sends only what it hears
        return Gain(point) > 0 && !Obstacles.Blocked(GlobalPosition, point);
    }

    // ---------- the bus ----------

    public void BeforeStep(ShipBody ship, double delta)
    {
        ShipBus bus = ship.Computer.Bus;
        bus[Slot, 0] = Type;
        bus[Slot, 1] = 1;
        bus[Slot, 2] = (byte)Mode;
        bus[Slot, 3] = (byte)(1 | (S.Deception ? 2 : 0));
        bus[Slot, 9] = (byte)((Sending ? 1 : 0) | (Mode != JamMode.Off && Powered < 1 ? 2 : 0) |
                              (Mode == JamMode.Deception && Deceiving ? 4 : 0));
        bus[Slot, 10] = (byte)Math.Round(Powered * 255);
    }

    public void AfterStep(ShipBody ship, double delta)
    {
        ShipBus bus = ship.Computer.Bus;
        if (bus.TakeWritten(Slot, 2))
            Set((JamMode)Math.Min((int)bus[Slot, 2], 2));
        bus.TakeWritten(Slot, 4);
        bus.TakeWritten(Slot, 5);
        bus.TakeWritten(Slot, 6);
        bus.TakeWritten(Slot, 7);
        if (bus.TakeWritten(Slot, 8))
        {
            if (bus[Slot, 8] == 0)
                Aimed = false;
            else
                Point(bus.Word(Slot, 4) / 65536f * Mathf.Tau, bus.Word(Slot, 6) / 65536f * Mathf.Tau);
        }
        Draw(delta);
    }

    /// <summary>Switches it: a model without deception makes noise when asked for it.</summary>
    public void Set(JamMode mode)
    {
        if (mode == JamMode.Deception && !S.Deception)
            mode = JamMode.Noise;
        if (mode != Mode)
            _pulls.Clear();
        Mode = mode;
    }

    /// <summary>Aims the beam: azimuth (right positive) and elevation (up positive), radians, ship axes.</summary>
    public void Point(float azimuth, float elevation)
    {
        Aimed = true;
        float flat = MathF.Cos(elevation);
        Aim = new Vector3(MathF.Sin(azimuth) * flat, MathF.Sin(elevation), -MathF.Cos(azimuth) * flat);
    }

    public void AllRound() => Aimed = false;

    /// <summary>The power it sends with, from the systems capacitor; all of it goes to heat.</summary>
    private void Draw(double delta)
    {
        if (Mode == JamMode.Off || _ship?.Wrecked == true)
        {
            Powered = Mode == JamMode.Off ? 1 : 0;
            return;
        }
        double wanted = S.Megawatts * delta;
        double got = _power?.DrawUpTo(PowerStore.Systems, wanted) ?? wanted;
        Powered = wanted > 0 ? (float)(got / wanted) : 1;
        _heat?.Add(got);
    }

    // ---------- a puppet (another machine's ship) ----------

    public void Follow(JamMode mode, bool aimed, Vector3 aim)
    {
        if (mode != Mode)
            _pulls.Clear();
        Mode = mode;
        Aimed = aimed;
        Aim = aim;
        Powered = 1;
    }
}
