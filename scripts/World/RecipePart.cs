using Godot;

namespace Xpace.World;

/// <summary>A component of a <see cref="Recipe"/>: a good or a model of outfit, and how much of it one run takes.</summary>
[GlobalClass]
public partial class RecipePart : Resource
{
    [Export(PropertyHint.ResourceType, "Good,Outfit")] public Resource Item;

    /// <summary>Tonnes of a good, or pieces of an outfit.</summary>
    [Export] public float Amount = 1f;
}
