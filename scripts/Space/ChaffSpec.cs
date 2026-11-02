using Godot;

namespace Xpace.Space;

/// <summary>
/// A model of chaff bundle (a .tres per model, in assets/weapons/decoys): the radar echo it blooms into.
/// </summary>
[GlobalClass]
public partial class ChaffSpec : Resource
{
    /// <summary>The model's name, as a program or a fittings list shows it.</summary>
    [Export] public string Model = "CHF";

    [ExportGroup("Echo")]
    /// <summary>m², a bit less than a light fighter.</summary>
    [Export] public float PeakCrossSection = 2.5f;
    [Export] public float BloomSeconds = 0.4f;
    [Export] public float LifeSeconds = 5f;
    /// <summary>m/s: how fast the strips drift apart.</summary>
    [Export] public float SpreadSpeed = 4f;
}
