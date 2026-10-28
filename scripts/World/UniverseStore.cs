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
///   stations/{callsign}.json one for each station
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
        Write(Path.Combine(folder, "universe.json"), new JsonObject { ["Version"] = state.Version, ["Clock"] = state.Clock });
        foreach (PilotRecord pilot in state.Pilots.Values)
            Write(Path.Combine(folder, "pilots", FileName(pilot.Id)), Stamped(pilot, state.Version));
        foreach (StationRecord station in state.Stations.Values)
            Write(Path.Combine(folder, "stations", FileName(station.Callsign)), Stamped(station, state.Version));
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
            if (!string.IsNullOrEmpty(station.Callsign))
                state.Stations[station.Callsign] = station;
        return state;
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
            Upgrade(kind, document, version);
            yield return document.Deserialize<T>(Json);
        }
    }

    /// <summary>
    /// Brings a document of <paramref name="kind"/> written in format <paramref name="from"/> up to the current one, a
    /// step at a time. Each change to the format that would lose or misread what an old document holds adds its step
    /// here (with <see cref="UniverseState.CurrentVersion"/> one higher); new fields with defaults need none.
    /// </summary>
    private static void Upgrade(string kind, JsonObject document, int from)
    {
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
