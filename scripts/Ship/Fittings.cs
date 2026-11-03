using System.Collections.Generic;
using Godot;
using Xpace.World;

namespace Xpace.Ship;

/// <summary>A hardpoint of a hull, as its exterior scene has it.</summary>
public sealed record HardpointPlace(string Name, int Size, MountKinds Accepts);

/// <summary>What is mounted on a ship's hardpoints: the hull's factory fitting, and what its pilot's record changed.</summary>
public static class Fittings
{
    private static readonly Dictionary<string, HardpointPlace[]> _places = new();

    /// <summary>
    /// A fitting from its key (<see cref="ShipBody.Fitted"/>: hardpoint=model path pairs), as another machine sends
    /// it: only models of weapons that go on hardpoints are taken (an empty key: nothing fitted).
    /// </summary>
    public static Dictionary<string, Resource> Parse(string key)
    {
        var fitted = new Dictionary<string, Resource>();
        foreach (string pair in (key ?? "").Split(';', System.StringSplitOptions.RemoveEmptyEntries))
        {
            int at = pair.IndexOf('=');
            if (at <= 0)
                continue;
            string path = pair[(at + 1)..];
            if (path.StartsWith("res://assets/weapons/") && ResourceLoader.Exists(path) && GD.Load<Resource>(path) is Resource model && IsPiece(model))
                fitted[pair[..at]] = model;
        }
        return fitted;
    }

    /// <summary>A model that goes on a hardpoint (a laser, a turret), not into a rack.</summary>
    public static bool IsPiece(Resource model) => model is LaserSpec or TurretSpec;

    /// <summary>The hardpoints of <paramref name="hull"/>, read from its exterior scene (<see cref="ShipSpec.Exterior"/>; none without one).</summary>
    public static HardpointPlace[] Places(ShipSpec hull)
    {
        string key = hull?.Exterior?.ResourcePath ?? "";
        if (_places.TryGetValue(key, out HardpointPlace[] known))
            return known;
        var places = new List<HardpointPlace>();
        if (hull?.Exterior?.Instantiate() is Node outside)
        {
            foreach (Node n in outside.FindChildren("*", "", true, false))
                if (n is Hardpoint h)
                    places.Add(new HardpointPlace(h.Name, h.Size, h.Accepts));
            outside.Free();
        }
        return _places[key] = places.ToArray();
    }

    /// <summary>
    /// The model on each hardpoint (by its name) of a ship with <paramref name="hull"/>, as <paramref name="record"/>
    /// has it (null: as it came from the factory). An empty hardpoint is not listed.
    /// </summary>
    public static Dictionary<string, Resource> Of(ShipSpec hull, ShipRecord record = null)
    {
        var fitted = new Dictionary<string, Resource>();
        if (hull?.Fitting != null)
            foreach ((string place, Resource model) in hull.Fitting)
                if (model != null)
                    fitted[place] = model;
        if (record?.Fittings != null)
            foreach ((string place, string path) in record.Fittings)
            {
                fitted.Remove(place);
                if (!string.IsNullOrEmpty(path) && ResourceLoader.Exists(path) && GD.Load<Resource>(path) is Resource model)
                    fitted[place] = model;
            }
        return fitted;
    }
}
