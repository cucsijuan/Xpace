using System;
using System.Collections.Generic;
using Godot;
using Xpace.Computer;
using Xpace.Net;
using Xpace.Space;

namespace Xpace.Ship;

/// <summary>
/// The ship's radar, bus slot 3 (130h). It looks in a cone ahead of the ship.
///
/// What it measures is only as good as its signal-to-noise ratio: the echo
/// falls with the fourth power of the distance and grows with the target's
/// radar cross-section; jammers add noise (they fall only with the square
/// of their distance, and count fully when they sit inside the beam).
/// With SNR, the errors are those of a monopulse radar: angle error
/// beamwidth / (1.6 sqrt(2 SNR)), range error resolution / sqrt(2 SNR).
/// Rocks (<see cref="RockField"/>): one between the antenna and a target
/// hides it; rocks in the target's resolution cell (beam, range, Doppler)
/// echo with it, and that clutter counts like noise. Rocks stand still, so
/// a target moving along the line of sight stands apart from them in
/// Doppler; one sitting still beside a rock is lost in its echo. The rocks
/// are never shown. A station's solids (<see cref="Solid"/>) hide what is
/// behind them too, and a ship inside a hangar until its door opens.
/// Below the detection threshold a target is not seen; below the lock
/// threshold a lock breaks. A better radar (more power: a longer detection
/// range; a narrower beam) measures better, and burns through jamming from
/// further away.
///
/// Search: twice a second it writes what it sees into a table in memory,
/// up to 16 contacts of 16 bytes: +0 id, +2 range (m), +4 azimuth,
/// +6 elevation (binary angles, 65536 to the turn; right and up positive),
/// +8 closing speed (1/8 m/s, from the Doppler), +10 flags (bit 0 locked).
///
/// Track: told to lock a contact by id, it measures that one 60 times a
/// second and writes where it is from the turret's pivot, in the ship's
/// axes: +0 id, +2 right, +4 up, +6 forward (1/4 m), +8 SNR (dB), +14 range
/// (m). Only the position: the velocity is for the program to work out.
/// A target lost at the edge of the cone (or to a weak echo) is taken back
/// by itself if it is seen again within <see cref="ReacquireSeconds"/>,
/// where the tracker last had it (within <see cref="ReacquireWindow"/> of
/// the gate, carried on at its velocity).
///
/// The tracker does not know which echo is the target: it looks in a gate
/// (one beamwidth across, one range resolution deep, one velocity
/// resolution wide in Doppler) that it moves along with the velocity it
/// has been following, and measures the middle of every echo in it,
/// weighted by their strength. The lock is on the strongest echo in the
/// gate. Chaff that leaves with nearly the target's radial speed sits in
/// the gate with it and pulls the middle toward itself, and may take the
/// lock; chaff thrown out at a different radial speed is filtered out by
/// the Doppler. A finer velocity resolution resists chaff better.
///
/// Jammers (<see cref="IJammer"/>): noise raises the noise floor, so an
/// echo is seen later and measured worse; the noise itself comes from the
/// jammer's direction, and the search writes it into the table as a
/// strobe: +2 range 0, +10 flags bit 1, numbered as the echo the jammer
/// rides on, after the echoes (it cannot be locked). A repeater
/// (<see cref="IDeceiver"/>) puts a copy of the tracked echo in the gate,
/// stronger than the echo, and walks it away; the tracker follows the
/// strongest, so the lock goes with the copy and is lost when it stops.
/// A finer range resolution or more power (the echo against the copy)
/// resists it.
///
/// Registers: +0 type 4  +2 mode (write: 0 off, 1 on)  +3 contacts in the
/// table  +4 sweeps so far  +5 locked (1/0)  +6 write: lock this id (0 lets
/// go)  +8 the locked id  +10 segment  +12 table offset  +14 track offset
/// </summary>
public partial class RadarDevice : Node3D, IShipDevice, INetRadar
{
    public const int Slot = 3;
    public const byte Type = 4;
    public const int MaxContacts = 16;

    [Export] public float ConeDegrees = 60f;
    [Export] public double SweepSeconds = 0.5;
    [Export] public double ReacquireSeconds = 3;
    /// <summary>How far from where the gate would be now a lost target may be to be taken back (m).</summary>
    [Export] public float ReacquireWindow = 300f;
    /// <summary>The power switch (a cockpit switch can work it): off, it sends nothing, whatever the program asks.</summary>
    [Export] public bool Powered { get; set; } = true;

    [ExportGroup("Quality")]
    /// <summary>Where a 10 m² target is just detected (more power, further).</summary>
    [Export] public float DetectionRange = 8000f;
    [Export] public float BeamwidthDegrees = 2.5f;
    [Export] public float RangeResolution = 15f;          // m
    [Export] public float VelocityResolution = 8f;        // m/s, Doppler
    [Export] public float DetectSnr = 20f;               // 13 dB
    [Export] public float LockSnr = 10f;                 // 10 dB
    [Export] public float SidelobeLevel = 0.01f;         // jamming from outside the beam
    /// <summary>Jamming this far over the noise in the search shows as a strobe (5 dB).</summary>
    [Export] public float StrobeJnr = 3f;

    /// <summary>Where the track is measured from: the turret's pivot.</summary>
    [Export] public NodePath TrackOriginPath;

    /// <summary>What warning receivers hear of this radar (a <see cref="RadarEmitter"/>: it does what the radar does).</summary>
    [Export] public NodePath EmitterPath;

    public const float ReferenceRcs = 10f;

    public IRadarTarget Locked { get; private set; }
    public bool On { get; private set; }
    /// <summary>The locked target's SNR this step (linear).</summary>
    public float LockedSnr { get; private set; }
    /// <summary>Where this step's track puts the locked target, in the world (noise and all): what a datalink passes on.</summary>
    public Vector3 TrackPosition { get; private set; }
    /// <summary>
    /// Counts the locks the program asked for. A lock the tracker carries
    /// on to another echo (chaff) keeps its number: to the datalink it is
    /// the same track.
    /// </summary>
    public int TrackNumber { get; private set; }

    // What the others can tell of it: in a session, and to warning receivers through its emitter
    public RadarMode Mode => !On ? RadarMode.Off : Locked != null ? RadarMode.Track : RadarMode.Search;
    public Node Tracked => RealLocked as Node is Node n && IsInstanceValid(n) ? n : null;

    /// <summary>What the lock is really on: the echo a repeater's copy in the gate copies.</summary>
    public IRadarTarget RealLocked => Locked is FalseEcho f ? f.Source : Locked;

    /// <summary>The launcher's datalink is guiding a missile on this radar's track (the launcher says so every step).</summary>
    public bool Guiding { get; set; }

    /// <summary>A puppet (another machine's ship): its emitter does what the owner's radar does.</summary>
    public void Follow(RadarMode mode, Node3D tracked, bool guiding) => _emitter?.Follow(mode, tracked, guiding);

    /// <summary>Its emitter goes quiet (the ship is a wreck).</summary>
    public void Silence() => _emitter?.Follow(RadarMode.Off, null, false);

    private RadarEmitter _emitter;

    /// <summary>An echo in the last sweep, where the radar put it (world): what the scope shows.</summary>
    public struct Echo
    {
        public ushort Id;
        public Vector3 Seen;
        public float Snr;
    }

    /// <summary>The echoes of the last sweep (the contact table, as the program reads it).</summary>
    public IReadOnlyList<Echo> Echoes => _echoes;

    private readonly List<Echo> _echoes = new();
    private readonly List<IRadarTarget> _contacts = new();
    private readonly List<IJammer> _strobes = new();
    private readonly Dictionary<IDeceiver, FalseEcho> _copies = new();
    private IRadarTarget _own;
    private IRadarTarget _lost;
    private double _lostFor;
    private Node3D _trackOrigin;

    /// <summary>What the track's positions are measured from (ship axes).</summary>
    public Node3D TrackOrigin => _trackOrigin ?? this;
    private Vector3 _gate, _gateVelocity, _centroid;
    private Vector3 _lostGate;                          // where the gate would be now, for a lost target   // the tracker's gate and its velocity, the echoes' middle (world)
    private double _sinceSweep = double.MaxValue;
    private byte _sweeps;
    private bool _registersSet;
    private readonly RandomNumberGenerator _rng = new() { Seed = 1981 };

    public override void _Ready()
    {
        for (Node n = GetParent(); n != null && _ship == null; n = n.GetParent())
            _ship = n as ShipBody;
        foreach (Node n in _ship?.GetChildren() ?? new Godot.Collections.Array<Node>())
            _own ??= n as IRadarTarget;
        _trackOrigin = TrackOriginPath == null || TrackOriginPath.IsEmpty ? this : GetNodeOrNull<Node3D>(TrackOriginPath) ?? this;
        _emitter = EmitterPath == null || EmitterPath.IsEmpty ? null : GetNodeOrNull<RadarEmitter>(EmitterPath);
        _emitter?.Follow(RadarMode.Off, null, false);
    }

    // ---------- the signal ----------

    /// <summary>Signal-to-noise ratio of a target's echo (linear), jamming and rock clutter included.</summary>
    public float Snr(IRadarTarget t)
    {
        if (t is FalseEcho copy)
            return copy.Snr;
        Rocks rocks = RocksAround(t);
        if (rocks.Hidden)
            return 0;
        Vector3 toTarget = t.GlobalPosition - GlobalPosition;
        float range = MathF.Max(toTarget.Length(), 1f);
        float perSquareMetre = DetectSnr * MathF.Pow(DetectionRange / range, 4) / ReferenceRcs;
        return perSquareMetre * t.RadarCrossSection / (1 + Jamming(toTarget) + perSquareMetre * rocks.Clutter);
    }

    private struct Rocks
    {
        public bool Hidden;
        public float Clutter;       // m², at the target's range
    }

    private readonly Dictionary<IRadarTarget, Rocks> _rocks = new();
    private ulong _rocksStep;
    private ShipBody _ship;

    /// <summary>What the rocks do to a target's echo: hide it, or echo with it (worked out once a step for each).</summary>
    private Rocks RocksAround(IRadarTarget t)
    {
        ulong step = Engine.GetPhysicsFrames();
        if (step != _rocksStep)
        {
            _rocks.Clear();
            _rocksStep = step;
        }
        if (_rocks.TryGetValue(t, out Rocks rocks))
            return rocks;
        rocks.Hidden = Obstacles.Blocked(GlobalPosition, t.GlobalPosition);
        if (!rocks.Hidden)
            rocks.Clutter = RockField.ClutterAll(GlobalPosition, _ship?.LinearVelocity ?? Vector3.Zero, t.GlobalPosition, t.Velocity,
                                                 Mathf.DegToRad(BeamwidthDegrees), RangeResolution, VelocityResolution);
        return _rocks[t] = rocks;
    }

    /// <summary>Jamming-to-noise ratio while looking along a direction.</summary>
    public float Jamming(Vector3 lookingAt) =>
        Space.Jamming.Noise(GetTree(), GlobalPosition, lookingAt, Mathf.DegToRad(BeamwidthDegrees), SidelobeLevel, _own);

    private float AngleError(float snr) => Mathf.DegToRad(BeamwidthDegrees) / (1.6f * MathF.Sqrt(2 * snr));
    private float RangeError(float snr) => RangeResolution / MathF.Sqrt(2 * snr);

    // ---------- every step ----------

    public unsafe void BeforeStep(ShipBody ship, double delta)
    {
        _emitter?.Follow(Mode, RealLocked as Node3D, Locked != null && Guiding);
        ShipBus bus = ship.Computer.Bus;
        if (!_registersSet)
        {
            _registersSet = true;
            bus[Slot, 0] = Type;
            bus[Slot, 1] = 2;
            bus.SetWord(Slot, 10, unchecked((short)ShipMemory.Segment));
            bus.SetWord(Slot, 12, ShipMemory.Contacts);
            bus.SetWord(Slot, 14, ShipMemory.Track);
        }
        On = Powered && bus[Slot, 2] != 0;
        byte* memory = ship.Computer.Memory + ShipMemory.Base;

        if (!On)
        {
            Locked = null;
            _lost = null;
            _contacts.Clear();
            _strobes.Clear();
            bus[Slot, 3] = 0;
        }
        else
        {
            _sinceSweep += delta;
            if (_sinceSweep >= SweepSeconds)
            {
                _sinceSweep = 0;
                Sweep(ship, memory + ShipMemory.Contacts);
                bus[Slot, 3] = (byte)(_contacts.Count + _strobes.Count);
                bus[Slot, 4] = ++_sweeps;
            }
            if (Gone(Locked))
                Locked = null;                      // freed (a ship gone from the sector, chaff blown away)
            if (Gone(_lost))
                _lost = null;
            if (Locked != null && !Follow(delta))
            {
                _lost = RealLocked;                 // remember it for a while (the echo, not a copy of it)
                _lostFor = 0;
                _lostGate = _gate;
                Locked = null;
            }
            else if (_lost != null)
            {
                _lostFor += delta;
                _lostGate += _gateVelocity * (float)delta;
                if (_lostFor > ReacquireSeconds || !_lost.Alive)
                    _lost = null;
                else if (InCone(_lost) && _lost.GlobalPosition.DistanceTo(_lostGate) <= ReacquireWindow && Snr(_lost) >= LockSnr)
                {
                    Locked = _lost;
                    _gate = Locked.GlobalPosition;
                    _gateVelocity = Locked.Velocity;
                    _lost = null;
                    if (!Follow(0))
                        Locked = null;
                }
            }
            if (Locked != null)
            {
                Locked.Paint();
                WriteTrack(ship, memory + ShipMemory.Track);
            }
        }
        bus[Slot, 5] = (byte)(Locked != null ? 1 : 0);
        bus.SetWord(Slot, 8, Locked?.RadarId ?? 0);
    }

    public void AfterStep(ShipBody ship, double delta)
    {
        ShipBus bus = ship.Computer.Bus;
        if (!(bus.TakeWritten(Slot, 6) | bus.TakeWritten(Slot, 7)))
            return;
        ushort id = (ushort)bus.Word(Slot, 6);
        IRadarTarget before = Locked;
        Locked = null;
        _lost = null;
        if (id == 0 || !On)
            return;
        foreach (IRadarTarget t in _contacts)
            if (!Gone(t) && t.RadarId == id && InCone(t) && Snr(t) >= LockSnr)
                Locked = t;
        if (Locked == null)
            return;
        if (Locked != (before is FalseEcho copy ? copy.Source : before))
            TrackNumber++;
        _gate = Locked.GlobalPosition;
        _gateVelocity = Locked.Velocity;
        if (!Follow(0))
            Locked = null;
    }

    /// <summary>
    /// One look through the tracking gate: moves it on at the velocity it
    /// follows, weighs every echo in it (place and Doppler), puts the lock on
    /// the strongest. False when nothing in it is strong enough.
    /// </summary>
    private bool Follow(double delta)
    {
        _gate += _gateVelocity * (float)delta;
        Vector3 toGate = _gate - GlobalPosition;
        float gateRange = toGate.Length();
        float beam = Mathf.DegToRad(BeamwidthDegrees);
        float total = 0, strongest = 0;
        Vector3 sum = Vector3.Zero, sumVelocity = Vector3.Zero;
        foreach (Node node in GetTree().GetNodesInGroup("radar_targets"))
        {
            if (node is not IRadarTarget t || !InCone(t))
                continue;
            Vector3 to = t.GlobalPosition - GlobalPosition;
            float range = to.Length();
            float across = toGate.AngleTo(to) / beam;
            float deep = (range - gateRange) / RangeResolution;
            float doppler = (t.Velocity - _gateVelocity).Dot(to / range) / VelocityResolution;
            // half power half a cell out, in each of the three
            float w = Snr(t) * MathF.Exp(-2.77f * (across * across + deep * deep + doppler * doppler));
            if (w < 1e-3f)
                continue;
            total += w;
            sum += t.GlobalPosition * w;
            sumVelocity += t.Velocity * w;
            if (w > strongest)
            {
                strongest = w;
                Locked = t;
            }
        }
        foreach (Node node in GetTree().GetNodesInGroup("jammers"))
        {
            if (node is not IDeceiver d || d.Carrier == null || d.Carrier == _own)
                continue;
            if (!_copies.TryGetValue(d, out FalseEcho copy))
                _copies[d] = copy = new FalseEcho(d);
            copy.Live = false;
            // a repeater answers only a radar tracking its carrier (or the copy it made)
            if (RealLocked != d.Carrier ||
                !d.FalseEcho(this, GlobalPosition, delta, out Vector3 at, out Vector3 moving, out float snrAt1Km))
                continue;
            Vector3 to = at - GlobalPosition;
            float range = MathF.Max(to.Length(), 1);
            float across = toGate.AngleTo(to) / beam;
            float deep = (range - gateRange) / RangeResolution;
            float doppler = (moving - _gateVelocity).Dot(to / range) / VelocityResolution;
            float km = range / 1000;
            copy.GlobalPosition = at;
            copy.Velocity = moving;
            copy.Snr = snrAt1Km / (km * km) / (1 + Jamming(to));
            copy.Live = true;
            if (!InCone(copy))
                continue;
            float w = copy.Snr * MathF.Exp(-2.77f * (across * across + deep * deep + doppler * doppler));
            if (w < 1e-3f)
                continue;
            total += w;
            sum += at * w;
            sumVelocity += moving * w;
            if (w > strongest)
            {
                strongest = w;
                Locked = copy;
            }
        }
        if (total < LockSnr)
            return false;
        _centroid = sum / total;
        _gateVelocity = _gateVelocity.Lerp(sumVelocity / total, 0.2f);
        LockedSnr = total;
        return true;
    }

    private static bool Gone(IRadarTarget t) => t is GodotObject o && !IsInstanceValid(o);

    private bool InCone(IRadarTarget t)
    {
        Vector3 local = GlobalBasis.Inverse() * (t.GlobalPosition - GlobalPosition);
        bool shown = t is not Node3D n || n.IsVisibleInTree();      // a destroyed ship's puppet hides
        return t.Alive && shown && local.Length() >= 1 && Vector3.Forward.AngleTo(local) <= Mathf.DegToRad(ConeDegrees);
    }

    private unsafe void Sweep(ShipBody ship, byte* table)
    {
        _contacts.Clear();
        _echoes.Clear();
        foreach (Node node in GetTree().GetNodesInGroup("radar_targets"))
        {
            if (node is not IRadarTarget t || !InCone(t) || _contacts.Count >= MaxContacts)
                continue;
            float snr = Snr(t);
            if (snr < DetectSnr)
                continue;
            Vector3 local = Measure(t.GlobalPosition, snr, GlobalPosition, GlobalBasis);
            byte* e = table + _contacts.Count * 16;
            _contacts.Add(t);
            _echoes.Add(new Echo { Id = t.RadarId, Seen = GlobalPosition + GlobalBasis * local, Snr = snr });
            float az = Mathf.Atan2(local.X, -local.Z);
            float el = Mathf.Atan2(local.Y, new Vector2(local.X, local.Z).Length());
            Vector3 relative = t.Velocity - ship.LinearVelocity;
            float closing = -relative.Dot((t.GlobalPosition - GlobalPosition).Normalized());
            Put(e, 0, t.RadarId);
            Put(e, 2, (int)Math.Min(local.Length(), 65535));
            Put(e, 4, Angle(az));
            Put(e, 6, Angle(el));
            Put(e, 8, Clamp16(closing * 8));
            Put(e, 10, t == RealLocked ? 1 : 0);
        }
        Strobes(ship, table);
        for (int i = _contacts.Count + _strobes.Count; i < MaxContacts; i++)
            for (int j = 0; j < 16; j++)
                table[i * 16 + j] = 0;
    }

    /// <summary>
    /// The jammers whose noise the search heard above the detection
    /// threshold, inside the cone: their bearing (as well as the beam tells
    /// it), no range, after the echoes in the table.
    /// </summary>
    private unsafe void Strobes(ShipBody ship, byte* table)
    {
        _strobes.Clear();
        foreach (Node node in GetTree().GetNodesInGroup("jammers"))
        {
            if (node is not IJammer j || j.Carrier == _own || _contacts.Count + _strobes.Count >= MaxContacts)
                continue;
            Vector3 to = j.GlobalPosition - GlobalPosition;
            float km = MathF.Max(to.Length(), 1f) / 1000f;
            float jn = j.NoiseToward(GlobalPosition) / (km * km);
            Vector3 local = GlobalBasis.Inverse() * to;
            if (jn < StrobeJnr || local.Length() < 1 || Vector3.Forward.AngleTo(local) > Mathf.DegToRad(ConeDegrees) ||
                node is Node3D { Visible: false } || Obstacles.Blocked(GlobalPosition, j.GlobalPosition))
                continue;
            local = Measure(j.GlobalPosition, jn, GlobalPosition, GlobalBasis);
            byte* e = table + (_contacts.Count + _strobes.Count) * 16;
            _strobes.Add(j);
            Put(e, 0, j.Carrier?.RadarId ?? 0);
            Put(e, 2, 0);
            Put(e, 4, Angle(Mathf.Atan2(local.X, -local.Z)));
            Put(e, 6, Angle(Mathf.Atan2(local.Y, new Vector2(local.X, local.Z).Length())));
            Put(e, 8, 0);
            Put(e, 10, 2);
        }
    }

    /// <summary>
    /// Where the radar thinks an echo is, in a frame at 'from' with axes
    /// 'axes' (Godot local: right, up, back): the true place, with the
    /// angle and range errors its SNR allows.
    /// </summary>
    private Vector3 Measure(Vector3 echo, float snr, Vector3 from, Basis axes)
    {
        Vector3 los = echo - GlobalPosition;
        float range = los.Length();
        Vector3 dir = los / range;
        // two angle errors, across the line of sight
        Vector3 across = dir.Cross(Vector3.Up);
        if (across.LengthSquared() < 1e-6f)
            across = dir.Cross(Vector3.Right);
        across = across.Normalized();
        Vector3 up = across.Cross(dir);
        float sigma = AngleError(snr);
        dir = (dir + across * _rng.Randfn(0, sigma) + up * _rng.Randfn(0, sigma)).Normalized();
        Vector3 seen = GlobalPosition + dir * (range + _rng.Randfn(0, RangeError(snr)));
        return axes.Inverse() * (seen - from);
    }

    private unsafe void WriteTrack(ShipBody ship, byte* track)
    {
        Vector3 p = Measure(_centroid, LockedSnr, _trackOrigin.GlobalPosition, ship.GlobalBasis);
        TrackPosition = _trackOrigin.GlobalPosition + ship.GlobalBasis * p;
        _gate = TrackPosition;                      // the gate goes where the echoes were measured
        Put(track, 0, Locked.RadarId);
        Put(track, 2, Clamp16(p.X * 4));
        Put(track, 4, Clamp16(p.Y * 4));
        Put(track, 6, Clamp16(-p.Z * 4));
        Put(track, 8, Clamp16(10 * MathF.Log10(MathF.Max(LockedSnr, 1))));
        Put(track, 10, 0);
        Put(track, 12, 0);
        Put(track, 14, (int)Math.Min(p.Length(), 65535));
    }

    private static int Angle(float radians) => (int)Mathf.Round(radians / Mathf.Tau * 65536);
    private static int Clamp16(float x) => (int)Math.Clamp(MathF.Round(x), -32767, 32767);

    private static unsafe void Put(byte* p, int offset, int value)
    {
        p[offset] = (byte)value;
        p[offset + 1] = (byte)(value >> 8);
    }
}
