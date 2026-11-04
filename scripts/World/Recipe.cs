using Godot;
using Xpace.Space;

namespace Xpace.World;

/// <summary>
/// What something is made of (a .tres per recipe, in assets/recipes): one run turns <see cref="Inputs"/> into
/// <see cref="OutputAmount"/> of <see cref="Output"/>. It says nothing of where or how fast: that is each station's
/// <see cref="FactoryLine"/>, so changing a recipe changes it at every station that makes the thing. A recipe with no
/// inputs is a mine's: taken from the ground, the only way anything comes into being.
/// </summary>
[GlobalClass]
public partial class Recipe : Resource
{
    /// <summary>What it makes: a good or a model of outfit.</summary>
    [Export(PropertyHint.ResourceType, "Good,Outfit")] public Resource Output;

    /// <summary>Tonnes of a good, or pieces of an outfit, that one run makes.</summary>
    [Export] public float OutputAmount = 1f;

    /// <summary>What one run takes.</summary>
    [Export] public RecipePart[] Inputs = System.Array.Empty<RecipePart>();

    /// <summary>It takes nothing: a mine's.</summary>
    public bool Mined => Inputs.Length == 0;

    /// <summary>The id of a good or an outfit, as a station's stock has it.</summary>
    public static string IdOf(Resource item) => item switch
    {
        Good good => good.Id,
        Outfit outfit => outfit.Id,
        _ => null,
    };

    /// <summary>The name of a good or an outfit, as a screen shows it.</summary>
    public static string NameOf(Resource item) => item switch
    {
        Good good => good.Name,
        Outfit outfit => outfit.Model,
        _ => "",
    };
}
