using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Godot;
using Xpace.Ship;
using Xpace.World;

namespace Xpace.Space;

/// <summary>What a station's registry terminal asks the station, as JSON on the "registry" topic.</summary>
public sealed class RegistryRequest
{
    /// <summary>
    /// Look: the terminal as it is. Open: a service session begins (the core is laid out for the pilot). Failed: a wrong
    /// service key. Bad: a carve where there was no good record. Write: the signatures carved from the core
    /// (<see cref="Records"/>) go into the ship's hold. Close: the session ends with nothing written.
    /// </summary>
    public const string Look = "look", Open = "open", Failed = "failed", Bad = "bad", Write = "write", Close = "close";

    /// <summary>The station, by its id.</summary>
    public string Station { get; set; }
    public string Do { get; set; } = Look;
    /// <summary>With Write: the callsigns of the signatures carved.</summary>
    public List<string> Records { get; set; } = new();
}

/// <summary>What the station tells a terminal a pilot is at.</summary>
public sealed class RegistryView
{
    public string Station { get; set; }
    public string Title { get; set; }
    /// <summary>The pilot's ship is in one of the station's hangars, for the data to go into its hold.</summary>
    public bool Present { get; set; }
    /// <summary>The core has signatures the pilot hasn't copied (or not lately).</summary>
    public bool Fresh { get; set; }
    /// <summary>The intrusion watch caught the pilot lately: the terminal won't open a service session for them.</summary>
    public bool Locked { get; set; }
    /// <summary>A service session is open: the core's layout (<see cref="Registries.Core"/>) and key come from these.</summary>
    public bool Session { get; set; }
    public int Seed { get; set; }
    public int Records { get; set; }
    public List<string> Known { get; set; } = new();
    public int KeyLength { get; set; }
    /// <summary>What the intrusion watch has against the pilot now, and how it adds up, in points of <see cref="TraceLimit"/>.</summary>
    public double Trace { get; set; }
    public double TraceLimit { get; set; }
    public double TracePerSecond { get; set; }
    public double TracePerFailure { get; set; }
    public double TracePerBadRecord { get; set; }
    /// <summary>Signatures to a tonne of registry data.</summary>
    public int RecordsPerTonne { get; set; }
    /// <summary>The ships that docked lately, newest first, and how long ago (seconds).</summary>
    public List<LogEntry> Logbook { get; set; } = new();
    public double Clock { get; set; }
    /// <summary>What the last Write put in the hold (t).</summary>
    public int Written { get; set; } = -1;
    public string Message { get; set; } = "";
}

/// <summary>
/// The registry of a station: the signatures of the ships it deals with, kept in the core of the terminals in its
/// hangars (scenes/stations/props/registry_terminal.tscn, each a computer of its own: <see cref="StationTerminal"/>).
/// Anyone sees the traffic logbook on them. A pilot who gets the terminal's service session open (the service keys, then
/// the service key) can carve signatures out of the core's memory and write them to the hold of their ship in one of
/// the station's hangars as registry data, a good the law forbids (assets/goods/registry_data.tres) that pirate bases
/// buy to make false callsigns from: a tonne for every <see cref="RecordsPerTonne"/> signatures, the core holding
/// <see cref="MaxTonnes"/> tonnes of them (more where the security is tighter: more ships, better records). The core
/// has new signatures for a pilot only <see cref="FreshHours"/> after they last wrote some.
///
/// The station's intrusion watch counts against a pilot: every second of a session, every wrong service key, every bad
/// record carved (<see cref="TracePerSecond"/>, <see cref="TracePerFailure"/>, <see cref="TracePerBadRecord"/>), and
/// forgets slowly while no session is open (<see cref="TraceForgetPerSecond"/>): closing one and opening another starts
/// nothing over. At <see cref="TraceLimit"/> the session is traced: the session is closed with nothing written, the terminal won't open another
/// for the pilot for <see cref="FreshHours"/>, and the station acts on whoever's ship it is: a false callsign is found
/// out, a ship that says who it is gets a bounty (<see cref="TheftBounty"/>), and one that doesn't is a ship nobody
/// can tell in the hangar (its <see cref="HangarWatch"/> notices it). Free people out of sight get their signatures by
/// <see cref="Registries.Copy"/> (between <see cref="MinTonnes"/> and <see cref="MaxTonnes"/>).
///
/// Only the universe's owner keeps the registry: a guest's terminal asks the host. Put it beside the station's radio and
/// security in the station's scene.
/// </summary>
public partial class StationRegistry : Node
{
    public const string Topic = "registry";

    [Export] public int MinTonnes = 2;
    [Export] public int MaxTonnes = 6;
    [Export] public int RecordsPerTonne = 2;
    [Export] public double FreshHours = 24;
    [Export] public long TheftBounty = 2500;

    [ExportGroup("Intrusion watch")]
    /// <summary>Hex digits of the service key.</summary>
    [Export] public int KeyLength = 4;
    [Export] public double TraceLimit = 100;
    [Export] public double TracePerSecond = 0.15;
    [Export] public double TracePerFailure = 1.5;
    [Export] public double TracePerBadRecord = 6;
    [Export] public double TraceForgetPerSecond = 0.1;

    [ExportGroup("Texts")]
    [Export] public string NotPresentText = "No ship of yours in a hangar here.";
    [Export] public string StaleText = "Nothing new in the registry since your last copy.";
    [Export] public string NoRoomText = "No room in your hold.";
    [Export] public string TooFewText = "Too few signatures for a tonne of data.";
    [Export] public string CopiedText = "{0} t of registry data written to your hold.";
    /// <summary>{0} the bounty.</summary>
    [Export] public string LoggedText = "Intrusion traced to your ship. A bounty of {0} credits is on you.";
    [Export] public string TracedText = "Intrusion traced. Service locked.";
    [Export] public string LockedText = "Service locked.";

    public StationRadio Radio { get; private set; }

    // The service sessions open: the core laid out for each pilot (the live signatures, by callsign)
    private readonly Dictionary<string, (int Seed, HashSet<string> Live)> _sessions = new();
    private readonly Dictionary<string, double> _traced = new();
    // What the watch has against each pilot, as of when
    private readonly Dictionary<string, (double Points, double At)> _trace = new();

    public override void _Ready()
    {
        foreach (Node n in GetParent()?.GetChildren() ?? new Godot.Collections.Array<Node>())
            Radio ??= n as StationRadio;
    }

    public override void _EnterTree() => Universe.Current?.Answer(Topic, Desk);

    private static string Desk(string pilot, string text)
    {
        RegistryRequest request;
        try
        {
            request = JsonSerializer.Deserialize<RegistryRequest>(text);
        }
        catch (JsonException)
        {
            return null;
        }
        return request != null && StationRadio.Called(request.Station)?.Registry is StationRegistry registry
            ? JsonSerializer.Serialize(registry.Serve(pilot, request))
            : null;
    }

    /// <summary>Signatures in the core, as many as <see cref="MaxTonnes"/> tonnes of data.</summary>
    public int Records => Math.Max(1, MaxTonnes * RecordsPerTonne);

    /// <summary>Does what the terminal asks, if it may be done, and shows the terminal as it is after.</summary>
    public RegistryView Serve(string pilotId, RegistryRequest request)
    {
        Universe universe = Universe.Current;
        UniverseState state = universe.State;
        PilotRecord pilot = state.Pilot(pilotId);
        StationRecord station = state.Station(Radio.Id);
        Node3D ship = universe.ShipOf(pilotId);
        bool present = Radio.DockedHere(ship) || Radio.Inside(ship);
        bool locked = _traced.TryGetValue(pilotId, out double tracedAt) && state.Clock - tracedAt < FreshHours * 3600;
        bool fresh = Registries.Fresh(station, pilotId, FreshHours, state.Clock);
        string message = "";
        int written = -1;
        double trace = TraceNow(pilotId, state.Clock);
        switch (request.Do)
        {
            case RegistryRequest.Open:
                if (locked)
                    message = LockedText;
                else if (!fresh)
                    message = StaleText;
                else if (!_sessions.ContainsKey(pilotId))
                {
                    int seed = Registries.Seed(Radio.Id, pilotId, station.RegistryCopied.GetValueOrDefault(pilotId, -1));
                    var (_, live) = Registries.Core(seed, Records, Known(station));
                    _sessions[pilotId] = (seed, live.Values.ToHashSet());
                }
                break;
            case RegistryRequest.Failed when _sessions.ContainsKey(pilotId):
                trace += TracePerFailure;
                break;
            case RegistryRequest.Bad when _sessions.ContainsKey(pilotId):
                trace += TracePerBadRecord;
                break;
            case RegistryRequest.Write when _sessions.TryGetValue(pilotId, out var session) && trace < TraceLimit:
                int room = universe.Room(pilot);
                int carved = request.Records.Distinct().Count(session.Live.Contains);
                int tonnes = Math.Min(room, carved / Math.Max(1, RecordsPerTonne));
                if (!present)
                    message = NotPresentText;
                else if (room <= 0)
                    message = NoRoomText;
                else if (tonnes <= 0)
                    message = TooFewText;
                else
                {
                    _sessions.Remove(pilotId);
                    station.RegistryCopied[pilotId] = state.Clock;
                    pilot.Ship.Load(Good.Id, tonnes);
                    written = tonnes;
                    message = string.Format(CopiedText, tonnes);
                    universe.Touch(pilot);
                }
                break;
            case RegistryRequest.Close:
                _sessions.Remove(pilotId);
                break;
        }
        if (trace >= TraceLimit && _sessions.Remove(pilotId))
        {
            _traced[pilotId] = state.Clock;
            locked = true;
            trace = 0;
            message = Caught(ship, pilot);
        }
        _trace[pilotId] = (trace, state.Clock);
        _sessions.TryGetValue(pilotId, out var open);
        return new RegistryView
        {
            Station = Radio.Id, Title = Radio.ShownName, Present = present, Fresh = fresh, Locked = locked,
            Session = open.Live != null, Seed = open.Seed, Records = Records, Known = Known(station), KeyLength = KeyLength,
            Trace = trace, TraceLimit = TraceLimit, TracePerSecond = TracePerSecond, TracePerFailure = TracePerFailure,
            TracePerBadRecord = TracePerBadRecord, RecordsPerTonne = RecordsPerTonne,
            Logbook = station.Logbook.AsEnumerable().Reverse().ToList(), Clock = state.Clock, Written = written, Message = message,
        };
    }

    // What the watch has against the pilot by now: more while a session is open, less while none is
    private double TraceNow(string pilot, double clock)
    {
        if (!_trace.TryGetValue(pilot, out var t))
            return 0;
        double since = Math.Max(0, clock - t.At);
        return _sessions.ContainsKey(pilot) ? t.Points + TracePerSecond * since : Math.Max(0, t.Points - TraceForgetPerSecond * since);
    }

    // The ships it knows, newest first: the first signatures in its core
    private static List<string> Known(StationRecord station) =>
        station.Logbook.AsEnumerable().Reverse().Select(e => e.Callsign).Distinct().ToList();

    // The intrusion watch caught a session: the station acts on whoever's ship it is
    private string Caught(Node3D ship, PilotRecord pilot)
    {
        StationSecurity security = Radio.Security;
        if (ship == null || security == null)
            return TracedText;
        var (kind, forged) = Universe.Current.IdentityOf(ship);
        if (kind == Identity.Forged && forged is { Burned: false })
        {
            security.Unmask(ship, forged);
            return TracedText;
        }
        if (security.Jurisdiction(ship) is (_, CrimeRecord record))
        {
            record.Bounty += TheftBounty;
            Universe.Current.Touch(pilot);
            return string.Format(LoggedText, TheftBounty);
        }
        foreach (HangarDock dock in Radio.Docks)
            if (dock.Holds(ship))
                dock.Watch?.Notice(ship);
        return TracedText;
    }

    /// <summary>The good its data is.</summary>
    public static Good Good => GoodsCatalog.Find("registry_data");
}
