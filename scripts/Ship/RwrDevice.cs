using System;
using System.Collections.Generic;
using Godot;
using Xpace.Computer;
using Xpace.Space;

namespace Xpace.Ship;

/// <summary>
/// The radar warning receiver, bus slot 7 (170h). Four spiral antennas round
/// the hull hear radar pulses from every side; it only listens, so it gives
/// nothing away.
///
/// A radar's main lobe on the ship makes a pulse train it can hear if the
/// signal-to-noise ratio (the emitter's power over the square of the
/// distance, times <see cref="Sensitivity"/>) reaches <see cref="DetectSnr"/>.
/// What it makes of it:
///
///   bearing   from the ratio of the signal in the antennas: off by a ripple
///             of their pattern (<see cref="BearingErrorDegrees"/>, the same
///             at the same angle) and by noise that grows as the signal
///             weakens (<see cref="BearingNoiseDegrees"/> / sqrt(SNR));
///   range     guessed from the strength, taking the emitter to have the
///             power its library lists for that signature: a stronger radar
///             than expected looks nearer. The strength flickers by
///             <see cref="PowerErrorDb"/> from pulse to pulse;
///   type      the signature, if the library knows it; 'U' if not;
///   lock      the pulses have the tracking waveform.
///
/// A searching radar is heard only as its beam passes, a ping now and then:
/// an emitter stays in the table for <see cref="HoldSeconds"/> after it was
/// last heard, where it was last heard.
///
/// A heat seeker sends nothing to hear, so the same box carries a missile
/// approach warner: ultraviolet sensors that see the plume of a burning
/// motor coming at the ship out to <see cref="PlumeRange"/> (not the
/// ship's own missiles: it knows their plumes). The range comes from how
/// bright the plume looks, give or take <see cref="PlumeRangeError"/>.
/// A missile that has burnt out coasts unseen. In the table as type 'M'.
///
/// Registers: +0 type 8  +1 version 2  +2 entries in the table  +3 updates
/// so far  +4 flags (bit 0 something has us locked, bit 1 a new emitter in
/// the last second, bit 2 a missile coming)  +6 segment  +8 table offset.
///
/// The table, up to 16 entries of 8 bytes: +0 id  +2 bearing (binary
/// angle, 65536 to the turn, from the nose, right positive, in the ship's
/// horizontal plane)  +4 range guessed (m)  +6 type (a letter)  +7 flags
/// (bit 0 locked on us, bit 1 heard this step, bit 2 new, bit 3 a missile).
/// </summary>
public partial class RwrDevice : Node3D, IShipDevice
{
    public const int Slot = 7;
    public const byte Type = 8, Version = 2;
    public const int MaxEmitters = 16, EntrySize = 8;
    public const byte FlagLocked = 1, FlagHeard = 2, FlagNew = 4, FlagMissile = 8;

    [ExportGroup("Quality")]
    [Export] public float Sensitivity = 1f;
    [Export] public float DetectSnr = 10f;
    [Export] public float BearingErrorDegrees = 6f;
    [Export] public float BearingNoiseDegrees = 30f;
    [Export] public float PowerErrorDb = 1.5f;
    [Export] public double HoldSeconds = 4;
    [Export] public double LockHoldSeconds = 0.5;
    [Export] public double NewSeconds = 1;
    /// <summary>Heard without a break (a lock), it averages its guesses over this long.</summary>
    [Export] public float SmoothSeconds = 1f;

    /// <summary>The signatures it knows, with the power (at 1 km) it expects of each.</summary>
    [Export] public Godot.Collections.Dictionary<string, float> Library = new() { { "D", 1e4f }, { "F", 4e4f } };
    /// <summary>The power it takes an unknown emitter to have, for the range.</summary>
    [Export] public float UnknownPower = 1e4f;

    [ExportGroup("Missile warner")]
    [Export] public float PlumeRange = 5000f;
    [Export] public float PlumeBearingDegrees = 3f;
    [Export] public float PlumeRangeError = 0.25f;      // a fraction of the range
    [Export] public double PlumeHoldSeconds = 0.5;
    [Export] public float PlumeSmoothSeconds = 0.25f;  // a missile closes fast: a short average

    public sealed class Contact
    {
        public IRadarEmitter Emitter;   // a radar heard
        public Missile Missile;         // or a plume seen
        public ushort Id;
        public float Bearing;           // radians, right positive
        public float Range;             // m, guessed
        public char Letter;
        public double Heard, LockHeard = double.MaxValue, Age;
        public bool HeardNow, WasHeard;
        public bool Locked;
        /// <summary>Times the beam came onto the ship (each sweep past it, or the start of a lock).</summary>
        public int Pings;
        public bool IsMissile => Missile != null;
    }

    public IReadOnlyList<Contact> Contacts => _contacts;
    public bool LockWarning { get; private set; }
    public bool MissileWarning { get; private set; }

    private readonly List<Contact> _contacts = new();
    private bool _registersSet;
    private byte _updates;
    private readonly RandomNumberGenerator _rng = new() { Seed = 4021 };

    public unsafe void BeforeStep(ShipBody ship, double delta)
    {
        ShipBus bus = ship.Computer.Bus;
        if (!_registersSet)
        {
            _registersSet = true;
            bus[Slot, 0] = Type;
            bus[Slot, 1] = Version;
            bus.SetWord(Slot, 6, unchecked((short)ShipMemory.Segment));
            bus.SetWord(Slot, 8, ShipMemory.Rwr);
        }

        foreach (Contact c in _contacts)
        {
            c.Heard += delta;
            c.LockHeard += delta;
            c.Age += delta;
            c.WasHeard = c.HeardNow;
            c.HeardNow = false;
        }
        foreach (Node node in GetTree().GetNodesInGroup("radar_emitters"))
            if (node is IRadarEmitter e && !ship.IsAncestorOf(node))
                Listen(ship, e, (float)delta);
        foreach (Node node in GetTree().GetNodesInGroup("missiles"))
            if (node is Missile m && m.Shooter != ship)
                Watch(ship, m, (float)delta);
        _contacts.RemoveAll(c => c.Heard > (c.IsMissile ? PlumeHoldSeconds : HoldSeconds) ||
                                 (c.IsMissile ? !IsInstanceValid(c.Missile) : c.Emitter is GodotObject o && !IsInstanceValid(o)));

        byte* table = ship.Computer.Memory + ShipMemory.Base + ShipMemory.Rwr;
        bool locked = false, fresh = false, missile = false;
        int n = Math.Min(_contacts.Count, MaxEmitters);
        for (int i = 0; i < MaxEmitters; i++)
        {
            byte* entry = table + i * EntrySize;
            if (i >= n)
            {
                for (int j = 0; j < EntrySize; j++)
                    entry[j] = 0;
                continue;
            }
            Contact c = _contacts[i];
            c.Locked = c.LockHeard <= LockHoldSeconds;
            bool isNew = c.Age <= NewSeconds;
            locked |= c.Locked;
            fresh |= isNew && !c.IsMissile;
            missile |= c.IsMissile;
            Put(entry, 0, c.Id);
            Put(entry, 2, (int)Mathf.Round(c.Bearing / Mathf.Tau * 65536));
            Put(entry, 4, (int)Math.Clamp(MathF.Round(c.Range), 0, 65535));
            entry[6] = (byte)c.Letter;
            entry[7] = (byte)((c.Locked ? FlagLocked : 0) | (c.HeardNow ? FlagHeard : 0) |
                              (isNew ? FlagNew : 0) | (c.IsMissile ? FlagMissile : 0));
        }
        LockWarning = locked;
        MissileWarning = missile;
        bus[Slot, 2] = (byte)n;
        bus[Slot, 3] = ++_updates;
        bus[Slot, 4] = (byte)((locked ? 1 : 0) | (fresh ? 2 : 0) | (missile ? 4 : 0));
    }

    public void AfterStep(ShipBody ship, double delta) { }

    /// <summary>The SNR an emitter's main lobe gives this receiver.</summary>
    public float Snr(IRadarEmitter e)
    {
        float km = MathF.Max(e.GlobalPosition.DistanceTo(GlobalPosition), 1f) / 1000f;
        return e.PowerAt1Km * Sensitivity / (km * km);
    }

    private void Listen(ShipBody ship, IRadarEmitter e, float dt)
    {
        if (!e.Illuminates(GlobalPosition))
            return;
        float snr = Snr(e);
        if (snr < DetectSnr)
            return;
        float bearing = TrueBearing(ship, e.GlobalPosition);
        bearing += Mathf.DegToRad(BearingErrorDegrees) * MathF.Sin(4 * bearing + 0.7f)
                 + Mathf.DegToRad(BearingNoiseDegrees) / MathF.Sqrt(snr) * _rng.Randfn();
        float heard = snr * MathF.Pow(10, _rng.Randfn(0, PowerErrorDb) / 10);
        string signature = e.Signature ?? "";
        bool known = Library.TryGetValue(signature, out float power);
        float range = 1000f * MathF.Sqrt((known ? power : UnknownPower) * Sensitivity / heard);

        Contact c = _contacts.Find(x => x.Emitter == e);
        if (c == null)
        {
            c = new Contact
            {
                Emitter = e,
                Id = e.EmitterId,
                Letter = known && signature.Length > 0 ? char.ToUpperInvariant(signature[0]) : 'U',
            };
            _contacts.Add(c);
        }
        Heard(c, bearing, range, dt);
        if (e.Tracking)
            c.LockHeard = 0;
    }

    /// <summary>The missile warner: a burning motor coming this way, in range.</summary>
    private void Watch(ShipBody ship, Missile m, float dt)
    {
        if (!m.Burning)
            return;
        Vector3 toUs = GlobalPosition - m.GlobalPosition;
        float range = toUs.Length();
        if (range > PlumeRange || range < 1 || (m.Velocity - ship.LinearVelocity).Dot(toUs) <= 0)
            return;
        float bearing = TrueBearing(ship, m.GlobalPosition) + Mathf.DegToRad(PlumeBearingDegrees) * _rng.Randfn();
        float guessed = range * MathF.Max(0.2f, 1 + PlumeRangeError * _rng.Randfn());
        Contact c = _contacts.Find(x => x.Missile == m);
        if (c == null)
        {
            c = new Contact { Missile = m, Id = (ushort)(0x8000 | (m.GetInstanceId() & 0x7FFF)), Letter = 'M' };
            _contacts.Add(c);
        }
        Heard(c, bearing, guessed, dt);
    }

    /// <summary>
    /// A new look at a contact: the first sets it; a new ping goes halfway to
    /// what it says; heard without a break, the guesses are averaged over
    /// SmoothSeconds.
    /// </summary>
    private void Heard(Contact c, float bearing, float range, float dt)
    {
        bearing = Mathf.Wrap(bearing, -Mathf.Pi, Mathf.Pi);
        if (c.Age == 0 && c.Heard == 0 && c.Pings == 0)
        {
            c.Bearing = bearing;
            c.Range = range;
        }
        else
        {
            float weight = c.WasHeard ? Math.Clamp(dt / (c.IsMissile ? PlumeSmoothSeconds : SmoothSeconds), 0, 1) : 0.5f;
            c.Bearing = Mathf.Wrap(c.Bearing + weight * Mathf.Wrap(bearing - c.Bearing, -Mathf.Pi, Mathf.Pi), -Mathf.Pi, Mathf.Pi);
            c.Range += weight * (range - c.Range);
        }
        if (!c.WasHeard)
            c.Pings++;
        c.Heard = 0;
        c.HeardNow = true;
    }

    private float TrueBearing(ShipBody ship, Vector3 point)
    {
        Vector3 local = ship.GlobalBasis.Inverse() * (point - GlobalPosition);
        return Mathf.Atan2(local.X, -local.Z);
    }

    private static unsafe void Put(byte* p, int offset, int value)
    {
        p[offset] = (byte)value;
        p[offset + 1] = (byte)(value >> 8);
    }
}
