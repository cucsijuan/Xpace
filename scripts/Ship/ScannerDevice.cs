using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using Godot;
using Xpace.Computer;
using Xpace.Space;
using Xpace.World;

namespace Xpace.Ship;

/// <summary>What a cargo scanner read of a ship.</summary>
public sealed class ScanReading
{
    public string Callsign { get; set; } = "";
    public int Capacity { get; set; }
    public int Tonnes { get; set; }
    /// <summary>Tonnes of each good aboard, by the good's id.</summary>
    public Dictionary<string, int> Cargo { get; set; } = new();
    /// <summary>The question it answers (on a guest, the answer comes later).</summary>
    public int Asked { get; set; }
}

/// <summary>
/// The cargo scanner, bus slot 20 (240h). It reads the hold of the ship the radar has locked, and the lock must stay on
/// that ship for the model's whole time (<see cref="ScannerSpec.Seconds"/>), within its range: a lock lost, or pulled
/// off by a deceiving jammer, starts it over. A pilot works it through a program, a ship's AI straight with
/// <see cref="Start"/>. Scanning is no crime. In a session the hold is read by the owner of the universe.
///
/// Registers: +0 type 22  +1 version 1  +2 status: bit 0 reading, bit 1 a reading is in the window, bit 2 it has a ship
/// locked it could read, bit 3 the last reading was broken off, bit 4 out of range  +3 progress, 0 to 255
/// +4 write: 1 start, 0 stop  +6 seconds left (word)  +8 segment of the window (D000h)  +10 its offset
/// (<see cref="ShipMemory.Scan"/>)
/// The window: +0 the ship's callsign (8 characters)  +8 capacity (t, word)  +10 carried (t, word)  +12 kinds of good
/// +16 up to <see cref="MaxKinds"/> entries as the hold's list (<see cref="CargoHold"/>).
/// </summary>
public partial class ScannerDevice : Node, IShipDevice, IRearmable
{
    public const int Slot = 20;
    public const byte Type = 22, Version = 1;
    public const int MaxKinds = 8, ListAt = 16;
    public const byte Reading = 1, HasReading = 2, HasTarget = 4, Broken = 8, OutOfRange = 16;

    [Export] public ScannerSpec Spec { get; set; }

    private ScannerSpec S => Spec ??= new ScannerSpec();

    /// <summary>The ship it is reading now.</summary>
    public Node3D Target { get; private set; }
    /// <summary>The ship <see cref="Result"/> was read from.</summary>
    public Node3D ResultOf { get; private set; }
    public ScanReading Result { get; private set; }
    public bool Scanning => Target != null;
    /// <summary>How far through it is, 0 to 1.</summary>
    public float Progress => S.Seconds > 0 ? (float)Math.Min(1, _held / S.Seconds) : 1;

    private RadarDevice _radar;
    private PowerDevice _power;
    private bool _wanted, _broken, _far, _dirty;
    private double _held;
    private int _asked;
    private string _askedOf;

    public void Rearm() => Stop();

    /// <summary>Starts on the ship the radar has locked, if any.</summary>
    public void Start() => _wanted = true;

    public void Stop()
    {
        _wanted = false;
        Target = null;
        _held = 0;
    }

    /// <summary>The ship the radar's lock is truly on: nothing on a deceiver's copy, on chaff or a rock.</summary>
    private Node3D Locked() =>
        _radar?.Locked is null or FalseEcho || _radar.Tracked is not Node tracked ? null : Combat.ShipOf(tracked);

    public void BeforeStep(ShipBody ship, double delta)
    {
        _radar ??= ship.Device<RadarDevice>();
        _power ??= ship.Device<PowerDevice>();
        Node3D locked = Locked();
        ShipBus bus = ship.Computer.Bus;
        _far = locked != null && locked.GlobalPosition.DistanceTo(ship.GlobalPosition) > S.Range;
        if (_wanted)
        {
            _wanted = false;
            if (locked != null && !_far)
            {
                Target = locked;
                _held = 0;
                _broken = false;
            }
        }
        if (Target != null)
        {
            if (!IsInstanceValid(Target) || locked != Target || _far)
            {
                Target = null;
                _held = 0;
                _broken = true;
            }
            else
            {
                float share = 1;
                if (_power != null && S.Megawatts > 0)
                    share = (float)(_power.DrawUpTo(PowerStore.Systems, S.Megawatts * delta) / (S.Megawatts * delta));
                _held += delta * share;
                if (_held >= S.Seconds)
                    Read(ship);
            }
        }
        TakeAnswer();
        bus[Slot, 0] = Type;
        bus[Slot, 1] = Version;
        bus[Slot, 2] = (byte)((Target != null ? Reading : 0) | (Result != null ? HasReading : 0) | (locked != null ? HasTarget : 0) | (_broken ? Broken : 0) | (_far ? OutOfRange : 0));
        bus[Slot, 3] = (byte)(Progress * 255);
        bus.SetWord(Slot, 6, (short)Math.Ceiling(Target != null ? S.Seconds - _held : S.Seconds));
        bus.SetWord(Slot, 8, unchecked((short)ShipMemory.Segment));
        bus.SetWord(Slot, 10, ShipMemory.Scan);
        if (_dirty)
            Show(ship);
    }

    public void AfterStep(ShipBody ship, double delta)
    {
        ShipBus bus = ship.Computer.Bus;
        if (!bus.TakeWritten(Slot, 4))
            return;
        if (bus[Slot, 4] == 1)
            Start();
        else
            Stop();
    }

    // The reading is done: the owner of the universe says what the hold has
    private void Read(ShipBody ship)
    {
        Node3D target = Target;
        Target = null;
        _held = 0;
        ResultOf = target;
        if (Universe.Current is not Universe universe)
            return;
        if (!universe.IsGuest)
        {
            Result = universe.Scan(target);
            _dirty = true;
            return;
        }
        if (Xpace.Net.SessionNode.Instance?.IdOf(target) is ushort id)
        {
            _asked++;
            _askedOf = Universe.ScanTopic;
            universe.Ask(Universe.ScanTopic, string.Create(CultureInfo.InvariantCulture, $"{id} {_asked}"));
        }
    }

    private void TakeAnswer()
    {
        if (_askedOf == null || Universe.Current?.Document(_askedOf) is not string text)
            return;
        try
        {
            if (JsonSerializer.Deserialize<ScanReading>(text) is ScanReading reading && reading.Asked == _asked)
            {
                Result = reading;
                _askedOf = null;
                _dirty = true;
            }
        }
        catch (JsonException)
        {
            _askedOf = null;
        }
    }

    private unsafe void Show(ShipBody ship)
    {
        _dirty = false;
        byte* window = ship.Computer.Memory + ShipMemory.Base + ShipMemory.Scan;
        string sign = (Result.Callsign ?? "").ToUpperInvariant().PadRight(8);
        for (int i = 0; i < 8; i++)
            window[i] = sign[i] is >= ' ' and <= '~' ? (byte)sign[i] : (byte)'?';
        window[8] = (byte)Result.Capacity;
        window[9] = (byte)(Result.Capacity >> 8);
        window[10] = (byte)Result.Tonnes;
        window[11] = (byte)(Result.Tonnes >> 8);
        window[12] = (byte)CargoHold.List(window + ListAt, Result.Cargo, MaxKinds);
    }
}
