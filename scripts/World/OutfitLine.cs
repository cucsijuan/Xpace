using Godot;
using Xpace.Space;

namespace Xpace.World;

/// <summary>
/// Something a station's arms dealer sells: a model of <see cref="Outfit"/> (a missile, chaff, a flare) and how many
/// the dealer wants to have. What makes more of it is one of the station's factories (<see cref="FactoryLine"/>).
/// </summary>
[GlobalClass]
public partial class OutfitLine : Resource
{
    [Export] public Outfit Item;

    /// <summary>How many the dealer wants to have: with as many, one is at the model's price, and the factory rests.</summary>
    [Export] public int Stock = 24;

    public string Id => Item?.Id;
}
