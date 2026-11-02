using Godot;

namespace Xpace.Ship;

/// <summary>
/// A model of cruise generator (a tier). Its top speed is not written
/// anywhere: it is where its push meets the veil it has thinned.
/// </summary>
[GlobalClass]
public partial class GeneratorSpec : Resource
{
    [Export] public string Model = "G-1";

    /// <summary>
    /// How thin it can make the veil: the drag falls to 1 over this at best, in a medium of density 1. Where the
    /// medium's density reaches this, it can thin nothing: it drops out (and will not start).
    /// </summary>
    [Export] public float Thinning = 33f;

    /// <summary>What it pushes the ship with in cruise, at full throttle, in place of the main engine (N).</summary>
    [Export] public float Push = 3_000_000f;

    /// <summary>What it draws from the engines capacitor at full throttle, if the ship has a power plant (MW).</summary>
    [Export] public float Megawatts = 6f;

    [ExportGroup("Against an interdictor")]
    /// <summary>Seconds it can bear a medium thicker than <see cref="Thinning"/> from a beam before it drops out.</summary>
    [Export] public float HoldSeconds = 4f;
    /// <summary>The share of a beam it cancels, tuned exactly to it.</summary>
    [Export] public float Rejection = 0.97f;
    /// <summary>How far off the beam's frequency it can be and still cancel any of it (0-255 band).</summary>
    [Export] public float TuneBand = 30f;
    /// <summary>The share of that band over which it cancels all it can.</summary>
    [Export] public float TuneFlat = 0.3f;
    /// <summary>How fast its frequency can move (band a second).</summary>
    [Export] public float TuneRate = 240f;

    [ExportGroup("")]
    /// <summary>Seconds charging before it starts to thin the veil.</summary>
    [Export] public double SpoolSeconds = 4;

    /// <summary>How fast it thins: the drag falls to about a third in this many seconds.</summary>
    [Export] public float ThinSeconds = 1.5f;

    /// <summary>
    /// What the wake it stirs in the medium adds to the ship's radar echo at its thinnest (m²): a cruising ship shows
    /// from far off (the range goes with the fourth root).
    /// </summary>
    [Export] public float WakeEcho = 300f;

    /// <summary>How hard the veil may brake the ship as it comes back (m/s²): the generator lets it come back no faster.</summary>
    [Export] public float BrakeAcceleration = 1000f;
}
