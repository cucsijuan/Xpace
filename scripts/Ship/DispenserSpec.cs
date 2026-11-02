using Godot;

namespace Xpace.Ship;

/// <summary>
/// A model of countermeasure dispenser (a .tres per model, in assets/weapons/decoys): how much it holds and how it throws. What it throws is the chaff and flare scenes it is given.
/// </summary>
[GlobalClass]
public partial class DispenserSpec : Resource
{
    /// <summary>The model's name, as a program or a fittings list shows it.</summary>
    [Export] public string Model = "DSP";

    [ExportGroup("Magazine")]
    /// <summary>Bundles it holds.</summary>
    [Export] public int Chaff = 10;
    /// <summary>Flares it holds.</summary>
    [Export] public int Flares = 10;

    [ExportGroup("Throw")]
    /// <summary>m/s.</summary>
    [Export] public float ChaffKick = 15f;
    /// <summary>m/s.</summary>
    [Export] public float FlareKick = 20f;
    /// <summary>Seconds between two of a kind.</summary>
    [Export] public double CycleSeconds = 0.25;
}
