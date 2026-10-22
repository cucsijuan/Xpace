using System;
using System.Collections.Generic;
using Godot;
using Xpace.Computer;
using Xpace.Space;

namespace Xpace.Ship;

/// <summary>
/// The ship's radar, bus slot 3 (130h). It looks in a cone ahead of the ship.
///
/// Search: twice a second it writes what it sees into a table in memory,
/// up to 16 contacts of 16 bytes: +0 id, +2 range (m), +4 azimuth,
/// +6 elevation (binary angles, 65536 to the turn; right and up positive),
/// +8 closing speed (1/8 m/s), +10 flags (bit 0 locked).
///
/// Track: told to lock a contact by id, it writes 60 times a second where
/// that one is from the turret's pivot, in the ship's axes: +0 id, +2 right,
/// +4 up, +6 forward (m), +8, +10, +12 its velocity relative to the ship
/// (1/8 m/s), +14 range (m). It lets go if the contact leaves the cone.
///
/// Registers: +0 type 4  +2 mode (write: 0 off, 1 on)  +3 contacts in the
/// table  +4 sweeps so far  +5 locked (1/0)  +6 write: lock this id (0 lets
/// go)  +8 the locked id  +10 segment  +12 table offset  +14 track offset
/// </summary>
public partial class RadarDevice : Node3D, IShipDevice
{
    public const int Slot = 3;
    public const byte Type = 4;
    public const int MaxContacts = 16;

    [Export] public float Range = 8000f;
    [Export] public float ConeDegrees = 60f;
    [Export] public double SweepSeconds = 0.5;
    [Export] public float AngleNoiseDegrees = 0.15f;
    [Export] public float RangeNoise = 0.003f;
    /// <summary>Where the track is measured from: the turret's pivot.</summary>
    [Export] public NodePath TrackOriginPath;

    public IRadarTarget Locked { get; private set; }
    public bool On { get; private set; }

    private readonly List<IRadarTarget> _contacts = new();
    private Node3D _trackOrigin;
    private double _sinceSweep = double.MaxValue;
    private byte _sweeps;
    private bool _registersSet;
    private readonly RandomNumberGenerator _rng = new() { Seed = 1981 };

    public override void _Ready()
    {
        _trackOrigin = TrackOriginPath == null || TrackOriginPath.IsEmpty ? this : GetNodeOrNull<Node3D>(TrackOriginPath) ?? this;
    }

    public unsafe void BeforeStep(ShipBody ship, double delta)
    {
        ShipBus bus = ship.Computer.Bus;
        if (!_registersSet)
        {
            _registersSet = true;
            bus[Slot, 0] = Type;
            bus[Slot, 1] = 1;
            bus.SetWord(Slot, 10, unchecked((short)ShipMemory.Segment));
            bus.SetWord(Slot, 12, ShipMemory.Contacts);
            bus.SetWord(Slot, 14, ShipMemory.Track);
        }
        On = bus[Slot, 2] != 0;
        byte* memory = ship.Computer.Memory + ShipMemory.Base;

        if (!On)
        {
            Locked = null;
            _contacts.Clear();
            bus[Slot, 3] = 0;
        }
        else
        {
            _sinceSweep += delta;
            if (_sinceSweep >= SweepSeconds)
            {
                _sinceSweep = 0;
                Sweep(ship, memory + ShipMemory.Contacts);
                bus[Slot, 3] = (byte)_contacts.Count;
                bus[Slot, 4] = ++_sweeps;
            }
            if (Locked != null && !Sees(Locked, out _))
                Locked = null;
            if (Locked != null)
                WriteTrack(ship, memory + ShipMemory.Track);
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
        Locked = null;
        if (id == 0 || !On)
            return;
        foreach (IRadarTarget t in _contacts)
            if (t.RadarId == id && Sees(t, out _))
                Locked = t;
    }

    /// <summary>Where the target is, from the radar, in the ship's axes (right, up, back = Godot's local).</summary>
    private bool Sees(IRadarTarget t, out Vector3 local)
    {
        local = GlobalBasis.Inverse() * (t.GlobalPosition - GlobalPosition);
        if (!t.Alive || local.Length() > Range || local.Length() < 1)
            return false;
        return Vector3.Forward.AngleTo(local) <= Mathf.DegToRad(ConeDegrees);
    }

    private unsafe void Sweep(ShipBody ship, byte* table)
    {
        _contacts.Clear();
        foreach (Node node in GetTree().GetNodesInGroup("radar_targets"))
        {
            if (node is not IRadarTarget t || !Sees(t, out Vector3 local) || _contacts.Count >= MaxContacts)
                continue;
            byte* e = table + _contacts.Count * 16;
            _contacts.Add(t);
            float noise = Mathf.DegToRad(AngleNoiseDegrees);
            float range = local.Length() * (1 + _rng.Randfn(0, RangeNoise));
            float az = Mathf.Atan2(local.X, -local.Z) + _rng.Randfn(0, noise);
            float el = Mathf.Atan2(local.Y, new Vector2(local.X, local.Z).Length()) + _rng.Randfn(0, noise);
            Vector3 relative = t.Velocity - ship.LinearVelocity;
            float closing = -relative.Dot((t.GlobalPosition - GlobalPosition).Normalized());
            Put(e, 0, t.RadarId);
            Put(e, 2, (int)Math.Min(range, 65535));
            Put(e, 4, Angle(az));
            Put(e, 6, Angle(el));
            Put(e, 8, Clamp16(closing * 8));
            Put(e, 10, t == Locked ? 1 : 0);
        }
        for (int i = _contacts.Count; i < MaxContacts; i++)
            for (int j = 0; j < 16; j++)
                table[i * 16 + j] = 0;
    }

    private unsafe void WriteTrack(ShipBody ship, byte* track)
    {
        Basis toShip = ship.GlobalBasis.Inverse();
        Vector3 p = toShip * (Locked.GlobalPosition - _trackOrigin.GlobalPosition);
        Vector3 v = toShip * (Locked.Velocity - ship.LinearVelocity);
        Put(track, 0, Locked.RadarId);
        Put(track, 2, Clamp16(p.X));
        Put(track, 4, Clamp16(p.Y));
        Put(track, 6, Clamp16(-p.Z));
        Put(track, 8, Clamp16(v.X * 8));
        Put(track, 10, Clamp16(v.Y * 8));
        Put(track, 12, Clamp16(-v.Z * 8));
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
