using System.Collections.Generic;
using Godot;
using Xpace.World;

namespace Xpace.Ship;

/// <summary>What is mounted on a ship's hardpoints: the hull's factory fitting, and what its pilot's record changed.</summary>
public static class Fittings
{
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
