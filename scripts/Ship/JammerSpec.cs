using Godot;

namespace Xpace.Ship;

/// <summary>
/// What a jammer model can do (a .tres per model, in assets/ship/jammers).
/// Every model makes noise; some can also repeat a tracking radar's pulses
/// to pull its gate away (<see cref="Deception"/>).
///
/// The figures are signal-to-noise ratios in a reference radar 1 km away,
/// in the main lobe of an antenna that sends all round; they fall with the
/// square of the distance (one way), where a skin echo falls with the
/// fourth power: close in, the echo wins (burn-through).
/// </summary>
[GlobalClass]
public partial class JammerSpec : Resource
{
    /// <summary>The model's name, as a program or a fittings list shows it.</summary>
    [Export] public string Model = "JAM";

    [ExportGroup("Noise")]
    /// <summary>The jamming-to-noise ratio it causes at 1 km, sending all round.</summary>
    [Export] public float NoiseAt1Km = 400f;

    [ExportGroup("Antenna")]
    /// <summary>Aimed, how much more it puts into its beam than all round (1: it cannot aim).</summary>
    [Export] public float BeamGain = 6f;
    /// <summary>The aimed beam's whole width.</summary>
    [Export] public float BeamDegrees = 60f;

    [ExportGroup("Deception")]
    /// <summary>It can repeat a tracking radar's pulses (range gate pull-off).</summary>
    [Export] public bool Deception = false;
    /// <summary>The copy's signal-to-noise ratio at 1 km, all round: against the skin echo's, whether it takes the gate.</summary>
    [Export] public float RepeaterAt1Km = 3000f;
    /// <summary>It sits on the skin echo this long first, so the tracker takes the copy for the target.</summary>
    [Export] public float CaptureSeconds = 0.6f;
    /// <summary>Then the copy's delay grows: the false range runs away at this acceleration (m/s²).</summary>
    [Export] public float PullAcceleration = 50f;
    /// <summary>How far it pulls the gate before it stops sending (m).</summary>
    [Export] public float MaxPull = 1200f;
    /// <summary>Silent this long after a pull: the gate is left on nothing.</summary>
    [Export] public float DropSeconds = 1.5f;

    [ExportGroup("Power")]
    /// <summary>What it draws from the systems capacitor while sending (MW); all of it ends as heat in the ship.</summary>
    [Export] public float Megawatts = 1f;

    [ExportGroup("What warning receivers hear")]
    /// <summary>The letter its noise shows on a warning receiver ("J").</summary>
    [Export] public string Signature = "J";
    /// <summary>Signal-to-noise ratio it gives a reference warning receiver 1 km away, all round.</summary>
    [Export] public float HeardAt1Km = 1e5f;
    /// <summary>The tone a warning receiver plays for it.</summary>
    [Export] public float ToneHz = 180f;
}
