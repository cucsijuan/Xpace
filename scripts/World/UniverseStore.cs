using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Xpace.World;

/// <summary>
/// Keeps a <see cref="UniverseState"/> on disk as a folder of JSON documents, one for each record, so the universe
/// can grow without one file growing with it:
///
///   universe.json            the format's version and the universe's clock
///   pilots/{id}.json         one for each pilot
///   stations/{id}.json       one for each station
///   organisations/{id}.json  one for each organisation
///   cargo_ships/{id}.json    one for each cargo ship
///   ship_orders/{id}.json    one for each ship on order
///
/// Each document is written whole to a new file that then takes the old one's place, so a crash leaves the old one or
/// the new one, never half of either. A document written by an older version is brought up to the current format
/// (<see cref="Upgrade"/>) as it loads; one from a newer version is refused.
/// </summary>
public static class UniverseStore
{
    public static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static void Save(UniverseState state, string folder)
    {
        Directory.CreateDirectory(Path.Combine(folder, "pilots"));
        Directory.CreateDirectory(Path.Combine(folder, "stations"));
        Directory.CreateDirectory(Path.Combine(folder, "organisations"));
        Directory.CreateDirectory(Path.Combine(folder, "cargo_ships"));
        Directory.CreateDirectory(Path.Combine(folder, "ship_orders"));
        foreach (string gone in Gone(folder, "cargo_ships", state.CargoShips.Keys).Concat(Gone(folder, "ship_orders", state.ShipOrders.Keys)).Concat(Gone(folder, "stations", state.Stations.Keys)))
            File.Delete(gone);
        Write(Path.Combine(folder, "universe.json"), new JsonObject { ["Version"] = state.Version, ["Clock"] = state.Clock });
        foreach (PilotRecord pilot in state.Pilots.Values)
            Write(Path.Combine(folder, "pilots", FileName(pilot.Id)), Stamped(pilot, state.Version));
        foreach (StationRecord station in state.Stations.Values)
            Write(Path.Combine(folder, "stations", FileName(station.Id)), Stamped(station, state.Version));
        foreach (OrganisationRecord organisation in state.Organisations.Values)
            Write(Path.Combine(folder, "organisations", FileName(organisation.Id)), Stamped(organisation, state.Version));
        foreach (CargoShipRecord ship in state.CargoShips.Values)
            Write(Path.Combine(folder, "cargo_ships", FileName(ship.Id)), Stamped(ship, state.Version));
        foreach (ShipOrderRecord order in state.ShipOrders.Values)
            Write(Path.Combine(folder, "ship_orders", FileName(order.Id)), Stamped(order, state.Version));
    }

    /// <summary>The universe kept in <paramref name="folder"/>, or a new one if there is none.</summary>
    public static UniverseState Load(string folder)
    {
        var state = new UniverseState();
        string top = Path.Combine(folder, "universe.json");
        if (!File.Exists(top))
            return state;
        JsonObject universe = Read(top);
        int version = universe["Version"]?.GetValue<int>() ?? 1;
        if (version > UniverseState.CurrentVersion)
            throw new InvalidDataException($"the universe in {folder} was saved by a newer version of the game ({version})");
        state.Clock = universe["Clock"]?.GetValue<double>() ?? 0;
        foreach (PilotRecord pilot in Documents<PilotRecord>(Path.Combine(folder, "pilots"), "pilot"))
            if (!string.IsNullOrEmpty(pilot.Id))
                state.Pilots[pilot.Id] = pilot;
        foreach (StationRecord station in Documents<StationRecord>(Path.Combine(folder, "stations"), "station"))
            if (!string.IsNullOrEmpty(station.Id))
                state.Stations[station.Id] = station;
        foreach (OrganisationRecord organisation in Documents<OrganisationRecord>(Path.Combine(folder, "organisations"), "organisation"))
            if (!string.IsNullOrEmpty(organisation.Id))
                state.Organisations[organisation.Id] = organisation;
        foreach (CargoShipRecord ship in Documents<CargoShipRecord>(Path.Combine(folder, "cargo_ships"), "cargo ship"))
            if (!string.IsNullOrEmpty(ship.Id))
                state.CargoShips[ship.Id] = ship;
        foreach (ShipOrderRecord order in Documents<ShipOrderRecord>(Path.Combine(folder, "ship_orders"), "ship order"))
            if (!string.IsNullOrEmpty(order.Id))
                state.ShipOrders[order.Id] = order;
        return state;
    }

    // Documents of records that are no more: a ship lost must not come back with the next load
    private static System.Collections.Generic.IEnumerable<string> Gone(string folder, string kind, System.Collections.Generic.IEnumerable<string> ids)
    {
        var kept = ids.Select(FileName).ToHashSet();
        return Directory.GetFiles(Path.Combine(folder, kind), "*.json").Where(f => !kept.Contains(Path.GetFileName(f))).ToList();
    }

    private static System.Collections.Generic.IEnumerable<T> Documents<T>(string folder, string kind)
    {
        if (!Directory.Exists(folder))
            yield break;
        foreach (string file in Directory.GetFiles(folder, "*.json").OrderBy(f => f, StringComparer.Ordinal))
        {
            JsonObject document = Read(file);
            int version = document["Version"]?.GetValue<int>() ?? 1;
            document.Remove("Version");
            if (Upgrade(kind, document, version))
                yield return document.Deserialize<T>(Json);
        }
    }

    /// <summary>
    /// Brings a document of <paramref name="kind"/> written in format <paramref name="from"/> up to the current one, a
    /// step at a time. Each change to the format that would lose or misread what an old document holds adds its step
    /// here (with <see cref="UniverseState.CurrentVersion"/> one higher); new fields with defaults need none.
    /// </summary>
    private static bool Upgrade(string kind, JsonObject document, int from)
    {
        if (from < 2)
        {
            // Stations were known by their callsigns: now by ids, and the first sector's and the second's big station
            // have new ones. The ships and pads of the time went by those callsigns: the fleets start again
            if (kind is "cargo ship" or "ship order")
                return false;
            if (kind == "station")
            {
                string id = document["Callsign"]?.GetValue<string>() ?? "";
                document["Id"] = id switch { "STATION" => "ALPHA", "KESTREL" => "DRYDOCK", _ => id };
                document["Callsign"] = "";
                document.Remove("CargoPads");
                document.Remove("PadsResting");
                document.Remove("Mustered");
            }
        }
        return true;
    }

    private static JsonObject Stamped<T>(T record, int version)
    {
        var document = new JsonObject { ["Version"] = version };
        foreach ((string key, JsonNode value) in JsonSerializer.SerializeToNode(record, Json)!.AsObject().ToList())
            document[key] = value?.DeepClone();
        return document;
    }

    private static JsonObject Read(string file) => JsonNode.Parse(File.ReadAllText(file))?.AsObject() ?? new JsonObject();

    private static void Write(string file, JsonNode document)
    {
        string fresh = file + ".new";
        File.WriteAllText(fresh, document.ToJsonString(Json), Encoding.UTF8);
        File.Move(fresh, file, overwrite: true);
    }

    /// <summary>An id as a file's name: letters, digits, '-' and '_' kept, anything else as its code.</summary>
    public static string FileName(string id)
    {
        var name = new StringBuilder();
        foreach (char c in id ?? "")
            name.Append(char.IsAsciiLetterOrDigit(c) || c is '-' or '_' ? c : $"%{(int)c:x2}");
        return name + ".json";
    }
}
