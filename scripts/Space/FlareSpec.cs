using Godot;

namespace Xpace.Space;

/// <summary>
/// A model of flare (a .tres per model, in assets/weapons/decoys): the heat it burns with. Bought at an arms dealer,
/// straight into the ship's dispenser.
/// </summary>
[GlobalClass]
public partial class FlareSpec : Outfit
{
    public FlareSpec()
    {
        Model = "FLR";
        Price = 90;
    }

    [ExportGroup("Burn")]
    /// <summary>A drone's motor is 50 from behind.</summary>
    [Export] public float PeakHeat = 45f;
    [Export] public float IgniteSeconds = 0.15f;
    [Export] public float BurnSeconds = 2f;

    public override string About => "Flare: burns brighter than your engines, for a heat seeker's eye";

    public override string Details => $"Heat {PeakHeat:0}   Burns {BurnSeconds:0.#} s";
}
