using Godot;

namespace Xpace.Space;

/// <summary>The sectors there are, and the one a new pilot starts in.</summary>
[GlobalClass]
public partial class SectorMap : Resource
{
    [Export] public Godot.Collections.Array<Sector> Sectors = new();
    /// <summary>The Id of the sector a new pilot starts in (empty: the first).</summary>
    [Export] public string Start = "";

    public Sector Find(string id)
    {
        foreach (Sector s in Sectors)
            if (s != null && s.Id == id)
                return s;
        return null;
    }

    public Sector First => Find(Start) ?? (Sectors.Count > 0 ? Sectors[0] : null);
}
