using Godot;

namespace Xpace.World;

/// <summary>
/// A station's factory or mine: the <see cref="Recipe"/> it runs and how fast. It draws its inputs from the station's
/// stock, stops without them, and rests while the market has all it wants of what it makes.
/// </summary>
[GlobalClass]
public partial class FactoryLine : Resource
{
    [Export] public Recipe Recipe;

    /// <summary>Runs of the recipe in an hour of universe time.</summary>
    [Export] public float PerHour = 1f;
}
