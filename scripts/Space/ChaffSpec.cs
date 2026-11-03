using Godot;

namespace Xpace.Space;

/// <summary>
/// A model of chaff bundle (a .tres per model, in assets/weapons/decoys): the radar echo it blooms into. Bought by the
/// bundle at an arms dealer, straight into the ship's dispenser.
/// </summary>
[GlobalClass]
public partial class ChaffSpec : Outfit
{
    public ChaffSpec()
    {
        Model = "CHF";
        Price = 60;
    }

    [ExportGroup("Echo")]
    /// <summary>m², a bit less than a light fighter.</summary>
    [Export] public float PeakCrossSection = 2.5f;
    [Export] public float BloomSeconds = 0.4f;
    [Export] public float LifeSeconds = 5f;
    /// <summary>m/s: how fast the strips drift apart.</summary>
    [Export] public float SpreadSpeed = 4f;

    public override string About => "Chaff: a cloud of foil a radar's tracker may take for you";

    public override string Details => $"Echo {PeakCrossSection:0.#} m²   Lasts {LifeSeconds:0} s";
}
