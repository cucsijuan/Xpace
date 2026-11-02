using Godot;

namespace Xpace.Space;

/// <summary>
/// A model of flare (a .tres per model, in assets/weapons/decoys): the heat it burns with.
/// </summary>
[GlobalClass]
public partial class FlareSpec : Resource
{
    /// <summary>The model's name, as a program or a fittings list shows it.</summary>
    [Export] public string Model = "FLR";

    [ExportGroup("Burn")]
    /// <summary>A drone's motor is 50 from behind.</summary>
    [Export] public float PeakHeat = 45f;
    [Export] public float IgniteSeconds = 0.15f;
    [Export] public float BurnSeconds = 2f;
}
