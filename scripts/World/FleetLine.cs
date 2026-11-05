using Godot;

namespace Xpace.World;

/// <summary>Ships a station's owner has working for that station when a universe starts.</summary>
[GlobalClass]
public partial class FleetLine : Resource
{
    [Export] public ShipModel Model;
    [Export] public int Count = 1;
    /// <summary>The owner has these only once it has lost ships and has the stomach for it (<see cref="Personality.EscortAbove"/>), not from the start.</summary>
    [Export] public bool WhenThreatened;
    /// <summary>What the owner has fitted on ships it orders: hardpoint name to piece (none: what the hull comes with).</summary>
    [Export] public Godot.Collections.Dictionary<string, Resource> Fitting = new();
}
