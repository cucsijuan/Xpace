using Godot;

namespace Xpace.Ship;

/// <summary>
/// What a main engine model is (a .tres per model, in assets/ship/engines):
/// its thrust, how its core heats and its cooling carries the heat away,
/// what it can take, and the firmware in its controller.
///
/// Temperatures in degrees C, heat in MJ, MW and MJ/K. The core takes the
/// heat of making thrust; it passes it to the coolant loop (the core's
/// conductance), and the radiators give the coolant's off to space (theirs,
/// driven harder by the engines' share of the power and by the cooling
/// boost). At steady thrust f (of the rating) the core settles at
/// sink + f heat (1 / radiators + 1 / core).
/// </summary>
[GlobalClass]
public partial class EngineSpec : Resource
{
    [Export] public string Model = "D-50";

    /// <summary>The controller's firmware.</summary>
    [Export(PropertyHint.File, "*.bin")] public string FirmwarePath = "res://disks/engine.bin";
    /// <summary>The controller's speed: instructions a second.</summary>
    [Export] public uint InstructionsPerSecond = 50_000;

    [ExportGroup("Thrust")]
    /// <summary>The thrust at the rating (N): what the controller gives at full, as it leaves the shipyard.</summary>
    [Export] public float RatedThrust = 1_000_000f;
    /// <summary>The most the engine can physically give (N), whatever its controller is told; nobody publishes it.</summary>
    [Export] public float MaxThrust = 1_600_000f;

    [ExportGroup("Heat")]
    /// <summary>Heat into the core at the rating (MW); it grows with the thrust.</summary>
    [Export] public float HeatAtRated = 8f;
    [Export] public float CoreCapacity = 0.3f;          // MJ/K
    [Export] public float CoreConductance = 0.1f;       // MW/K, core to coolant
    [Export] public float CoolantCapacity = 0.45f;      // MJ/K
    /// <summary>The radiators, coolant to space, with an even share of power to the engines (MW/K).</summary>
    [Export] public float RadiatorConductance = 0.03f;
    /// <summary>What the radiators bring the coolant down to with nothing to carry.</summary>
    [Export] public float SinkTemperature = 20f;

    [ExportGroup("Cooling")]
    /// <summary>The cooling's drive with no power to the engines, against an even share (four half pips of twelve).</summary>
    [Export] public float CoolingWithNoPips = 0.5f;
    /// <summary>The cooling's drive with the most power to the engines (eight half pips).</summary>
    [Export] public float CoolingWithMostPips = 1.5f;
    /// <summary>The cooling boost: the pumps and radiators driven this many times harder.</summary>
    [Export] public float BoostCooling = 1.5f;
    /// <summary>What the boost draws from the engines capacitor (MW).</summary>
    [Export] public float BoostMegawatts = 2f;

    [ExportGroup("What it can take")]
    /// <summary>Above this the core wears: the engine gives less, for good (until repaired).</summary>
    [Export] public float SafeTemperature = 300f;
    /// <summary>The share of its thrust it loses for each degree over safe, each second.</summary>
    [Export] public float WearPerDegreeSecond = 0.0001f;
    /// <summary>Here the engine fails, and takes the ship with it.</summary>
    [Export] public float FailTemperature = 400f;
    /// <summary>What the failure does to the hull (past the shields).</summary>
    [Export] public float FailDamage = 1000f;

    [ExportGroup("What others see")]
    /// <summary>Heat a seeker sees from the radiators, for each MW they give off.</summary>
    [Export] public float RadiatorGlowPerMegawatt = 3f;
    /// <summary>Heat a seeker sees from a core at the failing temperature (nothing at the safe one).</summary>
    [Export] public float CoreGlow = 60f;
}
