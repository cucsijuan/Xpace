using Godot;

namespace Xpace.World;

/// <summary>
/// A good a station's market deals in: how much of it the market wants to have, and what the station's people use up
/// of it. What the station makes is its factories' (<see cref="FactoryLine"/>).
/// </summary>
[GlobalClass]
public partial class MarketGood : Resource
{
    [Export] public Good Good;

    /// <summary>The tonnes the market wants to have: with as many, a tonne is at the good's base price.</summary>
    [Export] public int Stock = 100;

    /// <summary>Tonnes an hour of universe time the station uses up, apart from what its factories take.</summary>
    [Export] public float Uses;
}
