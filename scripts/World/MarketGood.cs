using Godot;

namespace Xpace.World;

/// <summary>A good a station's market deals in: how much of it the market wants to have, and what the station makes or uses of it.</summary>
[GlobalClass]
public partial class MarketGood : Resource
{
    [Export] public Good Good;

    /// <summary>The tonnes the market wants to have: with as many, a tonne is at the good's base price.</summary>
    [Export] public int Stock = 100;

    /// <summary>Tonnes an hour of universe time the station makes (more than 0) or uses up (less than 0).</summary>
    [Export] public float Rate;
}
