using System;
using System.Collections.Generic;
using System.Linq;
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
/// Registers: +0 type 18  +1 version 3  +2 contacts in the table
/// +3 updates so far  +4 status: bit 0 it holds a station's laws, bit 1 the
/// ship is inside their speed limit, bit 2 asking, bit 3 off, bit 4 inside
/// the station's protected zone, bit 5 cleared to a dock
/// +5 write: a request to the contact in +6 (1 its laws, 2 docking, 3
/// launch: let the docked ship go; +6 = FFh: the nearest station, or for a
/// launch the station whose dock it is)  +6 the request's contact  +7
/// messages heard so far  +8 segment  +10 the contacts' table  +12 the laws
/// +14 the messages (offsets).
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
/// +16 the speed limit's radius (10 m)  +18 the landing pad in that dock
/// (0 none).
///
/// The messages, the newest first, 4 of 128 bytes: +0 who sent it (8
/// characters)  +8 the words (120 characters, space-padded).
///
/// Version 3: the landing pad's guidance at D000:1900 (<see cref="ShipMemory.Pad"/>),
/// 32 bytes, from the beacon of the pad the ship is cleared to (within its
/// range): +0 flags (bit 0 guidance, bit 1 over the pad and turned as it is,
/// bit 2 low enough for the clamps, bit 3 held by the clamps, bit 4 docked,
/// bit 5 launch asked)  +1 the dock's number  +2 right  +4 up  +6 forward:
/// where the ship's middle goes, from it, in its own axes (1/16 m)  +8 yaw
/// right  +10 pitch up  +12 roll right: how it must turn to lie as the pad
/// (binary angles, 65536 to the turn)  +14 the pad's leeway across (1/16 m)
/// +16 its leeway in heading (binary angle)  +18 how high above its place
/// the clamps take a ship (1/16 m)  +20 the pad's number.
/// </summary>
public partial class RadioDevice : Node3D, IShipDevice, IRadioReceiver
{
    public const int Slot = 16;
    public const byte Type = 18, Version = 3;
    public const int PadSize = 32;
    public const byte PadGuidance = 1, PadAligned = 2, PadLow = 4, PadHeld = 8, PadDocked = 16, PadLaunch = 32;
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
    /// <summary>The landing pad in that dock it is cleared to (0: a hangar with no pads).</summary>
    public int Pad { get; private set; }
    /// <summary>The ship asked the station that cleared it to let it go (docked: its pad lets go once the door opens).</summary>
    public bool Launching { get; private set; }
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

    public void Cleared(StationRadio from, int dock, int pad)
    {
        if (!On)
            return;
        if (dock != Dock || pad != Pad || from != DockFrom)
            Launching = false;
        Dock = dock;
        Pad = dock == 0 ? 0 : pad;
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
        if (what == RadioRequest.Launch && Dock != 0 && station == DockFrom)
            Launching = true;
    }

    /// <summary>Asks the nearest station for its laws (a cockpit key can work it).</summary>
    public void AskLaws() => Ask(RadioRequest.Laws);

    /// <summary>Asks the nearest station for a dock.</summary>
    public void AskDocking() => Ask(RadioRequest.Docking);

    /// <summary>Asks the station whose dock the ship is in to let it go.</summary>
    public void AskLaunch() => Ask(RadioRequest.Launch, DockFrom);

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
            Pad = 0;
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
        laws[18] = (byte)Math.Clamp(Pad, 0, 255);

        byte* messages = ship.Computer.Memory + ShipMemory.Base + ShipMemory.Messages;
        for (int i = 0; i < Messages; i++)
        {
            byte* m = messages + i * MessageSize;
            int at = _heard.Count - 1 - i;
            PutText(m, at >= 0 ? _heard[at].From : "", Radio.CallsignLength);
            PutText(m + Radio.CallsignLength, at >= 0 ? _heard[at].Text.ToUpperInvariant() : "", MessageWords);
        }

        WritePad(ship);

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
        if (what is not (RadioRequest.Laws or RadioRequest.Docking or RadioRequest.Launch))
            return;
        int which = bus[Slot, 6];
        if (what == RadioRequest.Launch && which == NearestStation)
            AskLaunch();
        else if (which == NearestStation)
            Ask(what);
        else if (which < _contacts.Count && _contacts[which] is StationRadio station)
            Ask(what, station);
    }

    // What the beacon of the pad the ship is cleared to tells it
    private unsafe void WritePad(ShipBody ship)
    {
        byte* p = ship.Computer.Memory + ShipMemory.Base + ShipMemory.Pad;
        for (int i = 0; i < PadSize; i++)
            p[i] = 0;
        LandingPad pad = On && Dock != 0 ? DockFrom?.Docks.FirstOrDefault(d => d.Number == Dock)?.PadNumbered(Pad) : null;
        if (pad == null || !pad.Guide(ship, out LandingPad.Guidance g))
            return;
        p[0] = (byte)(PadGuidance | (g.Aligned ? PadAligned : 0) | (g.Low ? PadLow : 0) | (g.Held ? PadHeld : 0) |
                      (g.Docked ? PadDocked : 0) | (Launching ? PadLaunch : 0));
        p[1] = (byte)Math.Clamp(Dock, 0, 255);
        Put(p, 2, Sixteenths(g.Offset.X));
        Put(p, 4, Sixteenths(g.Offset.Y));
        Put(p, 6, Sixteenths(g.Offset.Z));
        Put(p, 8, Angle(g.Yaw));
        Put(p, 10, Angle(g.Pitch));
        Put(p, 12, Angle(g.Roll));
        Put(p, 14, Sixteenths(pad.Leeway));
        Put(p, 16, Angle(Mathf.DegToRad(pad.HeadingLeeway)));
        Put(p, 18, Sixteenths(pad.CaptureHeight));
        p[20] = (byte)Math.Clamp(Pad, 0, 255);
    }

    private static int Sixteenths(float metres) => (int)Math.Clamp(MathF.Round(metres * 16), -32767, 32767);

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
