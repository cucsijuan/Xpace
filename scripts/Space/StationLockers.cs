using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Godot;
using Xpace.World;

namespace Xpace.Space;

/// <summary>What a pilot asks a station's lockers (<see cref="StationLockers"/>), as JSON on the "locker" topic.</summary>
public sealed class LockerRequest
{
    public const string Look = "look", Store = "store", Take = "take";

    /// <summary>The station, by its callsign.</summary>
    public string Station { get; set; }
    public string Do { get; set; } = Look;
    /// <summary>Which item: its place in what the pilot carries (to store) or in the locker (to take).</summary>
    public int Index { get; set; }
}

/// <summary>What the lockers show a pilot: what they carry and what their locker here holds.</summary>
public sealed class LockerView
{
    public string Station { get; set; }
    public bool Present { get; set; }
    public int Slots { get; set; }
    public List<ItemRecord> Carried { get; set; } = new();
    public List<ItemRecord> Locker { get; set; } = new();
    public string Message { get; set; } = "";
}

/// <summary>
/// A station's lockers, beside its radio in the station's scene: each pilot has one here, apart from the one at any
/// other station, for what they don't want to carry (<see cref="StationRecord.Lockers"/>). A pilot stores and takes
/// things with their ship in one of the station's hangars; what they carry on them is limited
/// (<see cref="PilotRecord.InventorySlots"/>), a locker is not. Like the market, only the universe's owner keeps
/// them: a guest's kiosk asks the host (topic <see cref="Topic"/>).
/// </summary>
public partial class StationLockers : Node
{
    public const string Topic = "locker";

    [ExportGroup("Texts")]
    [Export] public string NotPresentText = "You must be at this station: on foot here, or your ship in a hangar here.";
    [Export] public string FullText = "You can't carry any more.";
    [Export] public string NothingText = "Nothing there.";
    /// <summary>{0} the item.</summary>
    [Export] public string StoredText = "{0} stored.";
    [Export] public string TakenText = "{0} taken.";

    public StationRadio Radio { get; private set; }

    private static readonly List<StationLockers> _all = new();

    public static StationLockers Of(string callsign) => _all.FirstOrDefault(m => m.Radio?.Sign == callsign);

    public override void _Ready()
    {
        foreach (Node n in GetParent()?.GetChildren() ?? new Godot.Collections.Array<Node>())
            Radio ??= n as StationRadio;
    }

    public override void _EnterTree()
    {
        _all.Add(this);
        Universe.Current?.Answer(Topic, Desk);
    }

    public override void _ExitTree() => _all.Remove(this);

    private static string Desk(string pilot, string text)
    {
        LockerRequest request;
        try
        {
            request = JsonSerializer.Deserialize<LockerRequest>(text);
        }
        catch (JsonException)
        {
            return null;
        }
        return request != null && Of(request.Station) is StationLockers lockers
            ? JsonSerializer.Serialize(lockers.Serve(pilot, request))
            : null;
    }

    /// <summary>Does what the pilot asks, if it may be done, and shows them what they carry and keep here after.</summary>
    public LockerView Serve(string pilotId, LockerRequest request)
    {
        Universe universe = Universe.Current;
        PilotRecord pilot = universe.State.Pilot(pilotId);
        List<ItemRecord> locker = universe.State.Station(Radio.Sign).LockerOf(pilotId);
        Node3D ship = universe.ShipOf(pilotId);
        bool present = Radio.DockedHere(ship) || Radio.Inside(ship) || universe.OnFootAt(pilotId) == Radio.Sign;
        string message = "";
        if (request.Do is LockerRequest.Store or LockerRequest.Take)
        {
            List<ItemRecord> from = request.Do == LockerRequest.Store ? pilot.Items : locker;
            List<ItemRecord> to = request.Do == LockerRequest.Store ? locker : pilot.Items;
            if (!present)
                message = NotPresentText;
            else if (request.Index < 0 || request.Index >= from.Count)
                message = NothingText;
            else if (request.Do == LockerRequest.Take && pilot.Items.Count >= PilotRecord.InventorySlots)
                message = FullText;
            else
            {
                ItemRecord item = from[request.Index];
                from.RemoveAt(request.Index);
                to.Add(item);
                message = string.Format(request.Do == LockerRequest.Store ? StoredText : TakenText, item.Name);
                universe.Touch(pilot);
            }
        }
        return new LockerView
        {
            Station = Radio.Sign, Present = present, Slots = PilotRecord.InventorySlots,
            Carried = pilot.Items.ToList(), Locker = locker.ToList(), Message = message,
        };
    }
}
