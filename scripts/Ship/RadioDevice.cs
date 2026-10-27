using System;
using System.Collections.Generic;
using Godot;
using Xpace.Computer;
using Xpace.Space;

namespace Xpace.Ship;

/// <summary>
/// The ship's radio, bus slot 16 (200h). It lists the radios it hears
/// (stations and ships, as far as each one's range times
/// <see cref="Sensitivity"/>), nearest first; keeps the laws the last
/// station sent it and the dock a station cleared it to; and keeps the last
/// messages it heard. The radio ranges the station it holds laws from, so it
/// knows when the ship is inside their zones.
///
/// Registers: +0 type 18  +1 version 2  +2 contacts in the table
/// +3 updates so far  +4 status: bit 0 it holds a station's laws, bit 1 the
/// ship is inside their speed limit, bit 2 asking, bit 3 off, bit 4 inside
/// the station's protected zone, bit 5 cleared to a dock
/// +5 write: a request to the contact in +6 (1 its laws, 2 docking; +6 =
/// FFh: the nearest station)  +6 the request's contact  +7 messages heard so
/// far  +8 segment  +10 the contacts' table  +12 the laws  +14 the messages
/// (offsets).
///
/// The table, up to 16 entries of 16 bytes: +0 callsign (8 characters,
/// space-padded)  +8 kind ('S' a station, 'V' a vessel)  +9 flags (bit 0 the
/// laws held are its, bit 1 inside its speed limit, bit 2 inside its zone,
/// bit 3 it cleared the ship to a dock)  +10 range (10 m)  +12 bearing
/// (binary angle, 65536 to the turn, from the nose, right positive, in the
/// ship's horizontal plane)  +14 elevation (binary angle, up positive).
///
/// The laws, 32 bytes: +0 the station's callsign  +8 speed limit (1/8 m/s)
/// +10 the protected zone's radius (10 m)  +12 range to the station (10 m)
/// +14 flags (as status bits 0, 1, 4, 5)  +15 the dock cleared to (0 none)
/// +16 the speed limit's radius (10 m).
///
/// The messages, the newest first, 4 of 128 bytes: +0 who sent it (8
/// characters)  +8 the words (120 characters, space-padded).
/// </summary>
public partial class RadioDevice : Node3D, IShipDevice, IRadioReceiver
{
    public const int Slot = 16;
    public const byte Type = 18, Version = 2;
    public const int MaxContacts = 16, EntrySize = 16, LawsSize = 32;
    public const int Messages = 4, MessageSize = 128, MessageWords = 120;
    public const byte NearestStation = 0xFF;
    public const byte StatusLaws = 1, StatusSpeedZone = 2, StatusAsking = 4, StatusOff = 8, StatusZone = 16, StatusCleared = 32;

    /// <summary>The power switch (a cockpit switch can work it): off, it hears and sends nothing.</summary>
    [Export] public bool Powered { get; set; } = true;
    /// <summary>Up to 8 characters; empty: the pilot's name (<see cref="PilotsName"/>) or the ship's node name.</summary>
    [Export] public string Callsign { get; set; } = "";
    /// <summary>The player's ship: with no callsign of its own, the radio sends the pilot's name.</summary>
    [Export] public bool PilotsName;
    /// <summary>How far a plain receiver hears this one, metres.</summary>
    [Export] public float Range = 20000f;
    /// <summary>It hears a radio as far as that radio's range times this.</summary>
    [Export] public float Sensitivity { get; set; } = 1f;
    /// <summary>A request with no answer in this long is given up.</summary>
    [Export] public double AskSeconds = 3;

    /// <summary>The laws it holds (null: none yet), and the station they came from.</summary>
    public StationLaws Laws { get; private set; }
    public StationRadio LawsFrom { get; private set; }
    /// <summary>The ship is inside the speed limit of the laws it holds.</summary>
    public bool InsideSpeedZone { get; private set; }
    /// <summary>The ship is inside the protected zone of the laws it holds.</summary>
    public bool InsideZone { get; private set; }
    /// <summary>The dock it is cleared to (0: none), and by whom.</summary>
    public int Dock { get; private set; }
    public StationRadio DockFrom { get; private set; }
    public IReadOnlyList<IRadio> Contacts => _contacts;
    /// <summary>What it heard, the newest last (the speech: how it was spoken, see <see cref="RadioVoicePack"/>).</summary>
    public IReadOnlyList<(string From, string Text, string Speech)> Heard => _heard;

    /// <summary>A message heard: who sent it, the words, how they are spoken.</summary>
    public event Action<string, string, string> MessageHeard;

    string IRadio.Callsign => Radio.Callsign(Callsign == "" && PilotsName ? Menu.MainMenu.PlayerName : Callsign, GetParent() ?? this);

    /// <summary>On a ship another machine flies (a puppet), it is that machine's radio: stations here leave it to that machine.</summary>
    bool IRadioReceiver.Relayed => Net.NetBody.IsPuppet(this);

    /// <summary>A puppet's radio: as the owner's is (its callsign, on or off).</summary>
    public void Follow(string callsign, bool on)
    {
        Callsign = callsign ?? "";
        PilotsName = false;
        Powered = on;
    }
    char IRadio.Kind => 'V';
    Vector3 IRadio.RadioPosition => GlobalPosition;
    float IRadio.RadioRange => Range;
    bool IRadio.RadioOn => On;

    private bool On => Powered && _ship?.Wrecked != true;

    private readonly List<IRadio> _contacts = new();
    private readonly List<(string From, string Text, string Speech)> _heard = new();
    private ShipBody _ship;
    private byte _updates, _messages;
    private double _asking;

    public override void _Ready()
    {
        AddToGroup(Radio.Group);
        _ship = Combat.ShipOf(this) as ShipBody;
    }

    public void Receive(StationRadio from, StationLaws laws)
    {
        if (!On)
            return;
        Laws = laws;
        LawsFrom = from;
        _asking = 0;
    }

    public void Cleared(StationRadio from, int dock)
    {
        if (!On)
            return;
        Dock = dock;
        DockFrom = dock == 0 ? null : from;
        _asking = 0;
    }

    public void Hear(IRadio from, string text, string speech)
    {
        if (!On)
            return;
        _heard.Add((from.Callsign, text, speech ?? ""));
        if (_heard.Count > Messages)
            _heard.RemoveAt(0);
        _messages++;
        MessageHeard?.Invoke(from.Callsign, text, speech ?? "");
    }

    /// <summary>Asks a station (null: the nearest heard) for something.</summary>
    public void Ask(RadioRequest what, StationRadio station = null)
    {
        station ??= Nearest();
        if (!On || station == null)
            return;
        station.Request(this, what);
        _asking = AskSeconds;
    }

    /// <summary>Asks the nearest station for its laws (a cockpit key can work it).</summary>
    public void AskLaws() => Ask(RadioRequest.Laws);

    /// <summary>Asks the nearest station for a dock.</summary>
    public void AskDocking() => Ask(RadioRequest.Docking);

    private StationRadio Nearest()
    {
        foreach (IRadio r in _contacts)
            if (r is StationRadio s)
                return s;
        return null;
    }

    public unsafe void BeforeStep(ShipBody ship, double delta)
    {
        _ship = ship;
        ShipBus bus = ship.Computer.Bus;
        bus[Slot, 0] = Type;
        bus[Slot, 1] = Version;
        bus.SetWord(Slot, 8, unchecked((short)ShipMemory.Segment));
        bus.SetWord(Slot, 10, ShipMemory.Radios);
        bus.SetWord(Slot, 12, ShipMemory.Laws);
        bus.SetWord(Slot, 14, ShipMemory.Messages);
        _asking = Math.Max(0, _asking - delta);
        if (LawsFrom != null && !IsInstanceValid(LawsFrom))
        {
            Laws = null;
            LawsFrom = null;
        }
        if (DockFrom != null && !IsInstanceValid(DockFrom))
        {
            Dock = 0;
            DockFrom = null;
        }

        _contacts.Clear();
        if (On)
            foreach (IRadio r in Radio.All(GetTree()))
                if (!ReferenceEquals(r, this) && Radio.Hears(this, r))
                    _contacts.Add(r);
        _contacts.Sort((a, b) => Distance(a).CompareTo(Distance(b)));
        if (_contacts.Count > MaxContacts)
            _contacts.RemoveRange(MaxContacts, _contacts.Count - MaxContacts);

        float lawsRange = LawsFrom?.GlobalPosition.DistanceTo(GlobalPosition) ?? float.MaxValue;
        InsideSpeedZone = Laws != null && lawsRange <= Laws.SpeedRadius;
        InsideZone = Laws != null && lawsRange <= Laws.ZoneRadius;
        byte* table = ship.Computer.Memory + ShipMemory.Base + ShipMemory.Radios;
        for (int i = 0; i < _contacts.Count; i++)
        {
            IRadio r = _contacts[i];
            byte* entry = table + i * EntrySize;
            PutName(entry, r.Callsign);
            entry[8] = (byte)r.Kind;
            bool theirs = ReferenceEquals(r, LawsFrom);
            entry[9] = (byte)((theirs ? 1 : 0) | (theirs && InsideSpeedZone ? 2 : 0) | (theirs && InsideZone ? 4 : 0) |
                              (Dock != 0 && ReferenceEquals(r, DockFrom) ? 8 : 0));
            Put(entry, 10, Tens(Distance(r)));
            Vector3 local = ship.GlobalBasis.Inverse() * (r.RadioPosition - GlobalPosition);
            Put(entry, 12, Angle(Mathf.Atan2(local.X, -local.Z)));
            Put(entry, 14, Angle(Mathf.Atan2(local.Y, new Vector2(local.X, local.Z).Length())));
        }

        byte status = (byte)((Laws != null ? StatusLaws : 0) | (InsideSpeedZone ? StatusSpeedZone : 0) |
                             (_asking > 0 ? StatusAsking : 0) | (Powered ? 0 : StatusOff) |
                             (InsideZone ? StatusZone : 0) | (Dock != 0 ? StatusCleared : 0));
        byte* laws = ship.Computer.Memory + ShipMemory.Base + ShipMemory.Laws;
        for (int i = 0; i < LawsSize; i++)
            laws[i] = 0;
        if (Laws != null)
        {
            PutName(laws, ((IRadio)LawsFrom).Callsign);
            Put(laws, 8, (int)Math.Clamp(MathF.Round(Laws.SpeedLimit * 8), 0, 32767));
            Put(laws, 10, Tens(Laws.ZoneRadius));
            Put(laws, 12, Tens(lawsRange));
            Put(laws, 16, Tens(Laws.SpeedRadius));
        }
        laws[14] = (byte)(status & (StatusLaws | StatusSpeedZone | StatusZone | StatusCleared));
        laws[15] = (byte)Math.Clamp(Dock, 0, 255);

        byte* messages = ship.Computer.Memory + ShipMemory.Base + ShipMemory.Messages;
        for (int i = 0; i < Messages; i++)
        {
            byte* m = messages + i * MessageSize;
            int at = _heard.Count - 1 - i;
            PutText(m, at >= 0 ? _heard[at].From : "", Radio.CallsignLength);
            PutText(m + Radio.CallsignLength, at >= 0 ? _heard[at].Text.ToUpperInvariant() : "", MessageWords);
        }

        bus[Slot, 2] = (byte)_contacts.Count;
        bus[Slot, 3] = ++_updates;
        bus[Slot, 4] = status;
        bus[Slot, 7] = _messages;
    }

    public void AfterStep(ShipBody ship, double delta)
    {
        ShipBus bus = ship.Computer.Bus;
        bus.TakeWritten(Slot, 6);
        if (!bus.TakeWritten(Slot, 5))
            return;
        var what = (RadioRequest)bus[Slot, 5];
        if (what is not (RadioRequest.Laws or RadioRequest.Docking))
            return;
        int which = bus[Slot, 6];
        if (which == NearestStation)
            Ask(what);
        else if (which < _contacts.Count && _contacts[which] is StationRadio station)
            Ask(what, station);
    }

    private float Distance(IRadio r) => r.RadioPosition.DistanceTo(GlobalPosition);

    private static int Tens(float metres) => (int)Math.Clamp(MathF.Round(metres / 10), 0, 65535);

    private static int Angle(float radians) => (int)Mathf.Round(radians / Mathf.Tau * 65536);

    private static unsafe void PutName(byte* p, string name) => PutText(p, name, Radio.CallsignLength);

    private static unsafe void PutText(byte* p, string text, int length)
    {
        for (int i = 0; i < length; i++)
            p[i] = (byte)(i < text.Length && text[i] >= 32 && text[i] < 127 ? text[i] : ' ');
    }

    private static unsafe void Put(byte* p, int offset, int value)
    {
        p[offset] = (byte)value;
        p[offset + 1] = (byte)(value >> 8);
    }
}
