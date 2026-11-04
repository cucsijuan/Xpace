using Godot;

namespace Xpace.World;

/// <summary>Ships a station's owner has working for that station when a universe starts.</summary>
[GlobalClass]
public partial class FleetLine : Resource
{
    [Export] public ShipModel Model;
    [Export] public int Count = 1;
}
