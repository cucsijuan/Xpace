using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using Godot;
using Xpace.Computer;
using Xpace.Ship;
using Xpace.World;

namespace Xpace.Space;

/// <summary>
/// The registry card of a station's terminal (<see cref="StationTerminal"/>), bus slot 0 (100h): what links the
/// terminal's program to the station's registry (<see cref="StationRegistry"/>, asked through the universe's owner).
/// It holds the traffic logbook and, while a service session is open, the registry's core and the service key. The
/// station's intrusion watch keeps its own count against the pilot; the card shows it as the station last told it and
/// as it goes on since (<see cref="Trace"/>), and asks the station again once it reaches the limit.
///
/// Registers: +0 type 30  +1 version 1  +2 status: bit 0 the station answered, bit 1 the pilot's ship is in a hangar
/// here, bit 2 the core has signatures new to the pilot, bit 3 service locked for the pilot, bit 4 a service session is
/// open, bit 5 the last session was traced, bit 6 waiting for the station  +3 the trace, 0 to 255 of the limit
/// +4 write: 1 open a service session, 2 a wrong service key, 3 carve the signature at the offset in +6, 4 write the
/// carved signatures to the ship's hold, 5 close the session  +6 offset into the core (word)  +8 the last carve:
/// 1 carved, 2 carved before, 3 no good record there  +9 signatures carved  +10 tonnes the last write put in the hold
/// (255 none yet)  +11 logbook entries  +12 the core's rows of 16 bytes (word)  +14 the key's hex digits
/// +15 signatures to a tonne
/// Memory (D000h): +1000h the logbook, newest first, 16 entries of 16 bytes (callsign 8, hangar, pad, minutes ago as a
/// word, 4 unused)  +1200h the station's name (32)  +1300h the service key  +1340h the station's last word (64)
/// +2000h the core.
/// </summary>
public partial class RegistryDevice : Node
{
    public const int Slot = 0;
    public const byte Type = 30, Version = 1;
    public const ushort Logbook = 0x1000, Title = 0x1200, Key = 0x1300, Words = 0x1340, CoreAt = 0x2000;
    public const int LogEntries = 16;
    public const byte Online = 1, Present = 2, Fresh = 4, Locked = 8, Open = 16, Traced = 32, Waiting = 64;
    public const byte CmdOpen = 1, CmdFailed = 2, CmdCarve = 3, CmdWrite = 4, CmdClose = 5;
    public const byte Carved = 1, Again = 2, Bad = 3;

    /// <summary>The station it serves, by id.</summary>
    public string Station { get; set; }

    /// <summary>Seconds between looks at the station while nothing is going on.</summary>
    [Export] public double LookSeconds = 5;

    public RegistryView View { get; private set; }
    /// <summary>Points the intrusion watch has against the session.</summary>
    public double Trace { get; private set; }
    public bool SessionOpen => _core != null;
    public IReadOnlyCollection<string> CarvedRecords => _carved;
    public bool WasTraced { get; private set; }

    private byte[] _core;
    private Dictionary<int, string> _live;
    private readonly HashSet<string> _carved = new();
    private string _key = "";
    private byte _lastCarve, _written = 255;
    private bool _waiting, _dirty = true;
    private double _sinceLook = double.MaxValue, _sinceView;

    public override void _EnterTree()
    {
        if (Universe.Current is Universe universe)
            universe.Changed += Changed;
    }

    public override void _ExitTree()
    {
        if (Universe.Current is Universe universe)
            universe.Changed -= Changed;
    }

    /// <summary>Puts what the program reads in the registers and memory.</summary>
    public unsafe void BeforeStep(ShipComputer computer, double delta)
    {
        ShipBus bus = computer.Bus;
        _sinceLook += delta;
        _sinceView += delta;
        if (_sinceLook >= LookSeconds && !_waiting)
            Ask(RegistryRequest.Look);
        if (_core != null && View != null)
        {
            Trace += View.TracePerSecond * delta;
            if (Trace >= View.TraceLimit && !_waiting)
                Ask(RegistryRequest.Look);      // the station says whether it has caught the session
        }
        RegistryView v = View;
        bus[Slot, 0] = Type;
        bus[Slot, 1] = Version;
        bus[Slot, 2] = (byte)((v != null ? Online : 0) | (v?.Present == true ? Present : 0) | (v?.Fresh == true ? Fresh : 0) |
                              (v?.Locked == true ? Locked : 0) | (_core != null ? Open : 0) | (WasTraced ? Traced : 0) | (_waiting ? Waiting : 0));
        bus[Slot, 3] = (byte)(v != null && v.TraceLimit > 0 ? Math.Clamp(Trace / v.TraceLimit * 255, 0, 255) : 0);
        bus[Slot, 8] = _lastCarve;
        bus[Slot, 9] = (byte)Math.Min(255, _carved.Count);
        bus[Slot, 10] = _written;
        bus[Slot, 11] = (byte)Math.Min(LogEntries, v?.Logbook.Count ?? 0);
        bus.SetWord(Slot, 12, (_core?.Length ?? 0) / 16);
        bus[Slot, 14] = (byte)_key.Length;
        bus[Slot, 15] = (byte)(v?.RecordsPerTonne ?? 1);
        byte* memory = computer.Memory + ShipMemory.Base;
        if (v != null)
        {
            // The logbook's ages move on between answers
            for (int i = 0; i < LogEntries && i < v.Logbook.Count; i++)
            {
                LogEntry e = v.Logbook[i];
                int minutes = (int)Math.Clamp((v.Clock + _sinceView - e.At) / 60, 0, 65535);
                memory[Logbook + i * 16 + 12] = (byte)minutes;
                memory[Logbook + i * 16 + 13] = (byte)(minutes >> 8);
            }
        }
        if (!_dirty)
            return;
        _dirty = false;
        Text(memory, Title, 32, v?.Title ?? "");
        Text(memory, Words, 64, v?.Message ?? "");
        Text(memory, Key, 16, _key);
        for (int i = 0; i < LogEntries; i++)
        {
            LogEntry e = v != null && i < v.Logbook.Count ? v.Logbook[i] : null;
            Text(memory, (ushort)(Logbook + i * 16), 8, e?.Callsign ?? "");
            memory[Logbook + i * 16 + 8] = (byte)(e?.Dock ?? 0);
            memory[Logbook + i * 16 + 9] = (byte)(e?.Pad ?? 0);
        }
        if (_core != null)
            for (int i = 0; i < _core.Length && CoreAt + i < 0x10000; i++)
                memory[CoreAt + i] = _core[i];
    }

    /// <summary>Acts on what the program wrote.</summary>
    public void AfterStep(ShipComputer computer, double delta)
    {
        ShipBus bus = computer.Bus;
        if (!bus.TakeWritten(Slot, 4))
            return;
        switch (bus[Slot, 4])
        {
            case CmdOpen:
                WasTraced = false;
                _written = 255;
                Ask(RegistryRequest.Open);
                break;
            case CmdFailed when _core != null:
                Trace += View.TracePerFailure;
                Ask(RegistryRequest.Failed);
                break;
            case CmdCarve when _core != null:
                Carve((ushort)bus.Word(Slot, 6));
                break;
            case CmdWrite when _core != null:
                Ask(RegistryRequest.Write, _carved.ToList());
                break;
            case CmdClose when _core != null:
                Close();
                Ask(RegistryRequest.Close);
                break;
        }
    }

    /// <summary>The signature carved at that offset into the core: good, carved before, or no good record there.</summary>
    public byte Carve(int offset)
    {
        if (_core == null)
            return 0;
        if (_live.TryGetValue(offset, out string sign))
            _lastCarve = _carved.Add(sign) ? Carved : Again;
        else
        {
            _lastCarve = Bad;
            Trace += View.TracePerBadRecord;
            Ask(RegistryRequest.Bad);
        }
        return _lastCarve;
    }

    /// <summary>The service key of the session open (empty: none).</summary>
    public string ServiceKey => _key;

    /// <summary>The core of the session open, by offset: its live signatures.</summary>
    public IReadOnlyDictionary<int, string> Live => _live;

    private void Close()
    {
        _core = null;
        _live = null;
        _carved.Clear();
        _key = "";
        _dirty = true;
    }

    private void Ask(string what, List<string> records = null)
    {
        if (Station == null || Universe.Current is not Universe universe)
            return;
        _sinceLook = 0;
        _waiting = true;
        universe.Ask(StationRegistry.Topic, JsonSerializer.Serialize(new RegistryRequest { Station = Station, Do = what, Records = records ?? new() }));
    }

    private void Changed(string topic)
    {
        if (topic != StationRegistry.Topic || Universe.Current?.Document(topic) is not string text)
            return;
        RegistryView view;
        try
        {
            view = JsonSerializer.Deserialize<RegistryView>(text);
        }
        catch (JsonException)
        {
            return;
        }
        if (view?.Station != Station)
            return;
        _waiting = false;
        bool wasOpen = _core != null;
        View = view;
        _sinceView = 0;
        _dirty = true;
        Trace = view.Trace;
        if (wasOpen && !view.Session && view.Locked && view.Written < 0)
            WasTraced = true;                   // caught: closed with nothing written
        if (view.Written >= 0)
        {
            _written = (byte)Math.Min(254, view.Written);
            Close();
        }
        if (view.Session && _core == null && !WasTraced)
        {
            (_core, _live) = Registries.Core(view.Seed, view.Records, view.Known);
            _key = Registries.Key(view.Seed, view.KeyLength);
            _carved.Clear();
            _lastCarve = 0;
        }
        else if (!view.Session && _core != null)
            Close();                            // the station ended it (another terminal, a restart)
    }

    private static unsafe void Text(byte* memory, ushort at, int length, string text)
    {
        byte[] bytes = Encoding.ASCII.GetBytes(text.ToUpperInvariant());
        for (int i = 0; i < length; i++)
            memory[at + i] = i < bytes.Length ? bytes[i] : (byte)0;
    }
}
