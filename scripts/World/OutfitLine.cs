using Godot;
using Xpace.Space;

namespace Xpace.World;

/// <summary>
/// Something a station's factory makes and its arms dealer sells: a missile model, how many the dealer wants to have,
/// how fast the factory makes them, and of what. The factory draws its input from what the station's market has of
/// that good, and stops without it.
/// </summary>
[GlobalClass]
public partial class OutfitLine : Resource
{
    [Export] public MissileSpec Item;

    /// <summary>How many the dealer wants to have: with as many, one is at the model's price, and the factory rests.</summary>
    [Export] public int Stock = 24;

    /// <summary>How many the factory makes in an hour of universe time.</summary>
    [Export] public float PerHour = 12f;

    /// <summary>What it is made of (none: of nothing the market deals in).</summary>
    [Export] public Good Input;

    /// <summary>Tonnes of the input that go into one.</summary>
    [Export] public float InputPerUnit = 0.5f;

    public string Id => Item.ResourcePath.GetFile().GetBaseName();
}
