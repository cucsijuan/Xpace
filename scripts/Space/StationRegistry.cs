using System;
using System.Text.Json;
using Godot;
using Xpace.Ship;
using Xpace.World;

namespace Xpace.Space;

/// <summary>What a pilot asks a station's registry terminal, as JSON on the "registry" topic.</summary>
public sealed class RegistryRequest
{
    public const string Look = "look", Start = "start", Finish = "finish";

    /// <summary>The station, by its id.</summary>
    public string Station { get; set; }
    public string Do { get; set; } = Look;
}

/// <summary>What the terminal shows a pilot.</summary>
public sealed class RegistryView
{
    public string Station { get; set; }
    public string Title { get; set; }
    /// <summary>The pilot's ship is in one of the station's hangars, for the data to go into its hold.</summary>
    public bool Present { get; set; }
    /// <summary>How long a copy takes, and how far along the pilot's is (seconds; below 0: none going).</summary>
    public double CopySeconds { get; set; }
    public double Copying { get; set; } = -1;
    public string Message { get; set; } = "";
}

/// <summary>
/// The registry of a station: the records of the ships it deals with, kept on a terminal in its hangars
/// (scenes/stations/props/registry_terminal.tscn). Copied onto data cores it is registry data, a good the law forbids
/// (assets/goods/registry_data.tres) that pirate bases buy to make false callsigns from. A pilot copies it standing at
/// the terminal for <see cref="CopySeconds"/>, into the hold of their ship in one of the station's hangars: between
/// <see cref="MinTonnes"/> and <see cref="MaxTonnes"/> (more where the security is tighter: more ships, better
/// records), as much as the hold has room for. It is new data only <see cref="FreshHours"/> after the pilot last
/// copied it. The terminal logs who uses it: a pilot the station cleared in (it knows who they are) gets a bounty for
/// it (<see cref="TheftBounty"/>); one who slipped in unseen, nothing. Only the universe's owner keeps the registry: a
/// guest's terminal asks the host.
///
/// Put it beside the station's radio and security in the station's scene.
/// </summary>
public partial class StationRegistry : Node
{
    public const string Topic = "registry";

    [Export] public int MinTonnes = 2;
    [Export] public int MaxTonnes = 6;
    [Export] public double CopySeconds = 20;
    [Export] public double FreshHours = 24;
    [Export] public long TheftBounty = 2500;

    [ExportGroup("Texts")]
    [Export] public string NotPresentText = "No ship of yours in a hangar here.";
    [Export] public string StaleText = "Nothing new in the registry since your last copy.";
    [Export] public string NoRoomText = "No room in your hold.";
    [Export] public string CopiedText = "{0} t of registry data copied to your hold.";
    /// <summary>{0} the bounty.</summary>
    [Export] public string LoggedText = "Unauthorised access logged. A bounty of {0} credits is on you.";
    [Export] public string NotDoneText = "Copy interrupted.";

    public StationRadio Radio { get; private set; }

    private readonly System.Collections.Generic.Dictionary<string, double> _started = new();

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

    /// <summary>Does what the pilot asks, if it may be done, and shows the terminal as it is after.</summary>
    public RegistryView Serve(string pilotId, RegistryRequest request)
    {
        Universe universe = Universe.Current;
        UniverseState state = universe.State;
        PilotRecord pilot = state.Pilot(pilotId);
        StationRecord station = state.Station(Radio.Id);
        Node3D ship = universe.ShipOf(pilotId);
        bool present = Radio.DockedHere(ship) || Radio.Inside(ship);
        string message = "";
        if (request.Do == RegistryRequest.Start)
        {
            if (!present)
                message = NotPresentText;
            else if (station.RegistryCopied.TryGetValue(pilotId, out double at) && state.Clock - at < FreshHours * 3600)
                message = StaleText;                    // as Registries.Copy has it
            else
                _started[pilotId] = state.Clock;
        }
        else if (request.Do == RegistryRequest.Finish && _started.Remove(pilotId, out double began))
        {
            int room = universe.Room(pilot);
            if (!present || state.Clock - began < CopySeconds - 0.5)
                message = NotDoneText;
            else if (room <= 0)
                message = NoRoomText;
            else if (Registries.Copy(station, pilotId, MinTonnes, MaxTonnes, FreshHours, room, state.Clock) is int tonnes and > 0)
            {
                pilot.Ship.Load(Good.Id, tonnes);
                message = string.Format(CopiedText, tonnes);
                if (Radio.Expects(ship) && Radio.Security?.Jurisdiction(ship) is (_, CrimeRecord record))
                {
                    record.Bounty += TheftBounty;           // it knows who was docked here
                    message += " " + string.Format(LoggedText, TheftBounty);
                }
                universe.Touch(pilot);
            }
        }
        return new RegistryView
        {
            Station = Radio.Id, Title = Radio.ShownName, Present = present, CopySeconds = CopySeconds, Message = message,
            Copying = _started.TryGetValue(pilotId, out double since) ? state.Clock - since : -1,
        };
    }

    /// <summary>The good its data is.</summary>
    public static Good Good => GoodsCatalog.Find("registry_data");
}
