using System.Text.Json.Serialization;
using Godot;

namespace Xpace.Ship;

/// <summary>
/// A cruise generator at work, whoever works it and wherever: the rules against the medium and an interdictor's beam,
/// the same for a ship's <see cref="CruiseDevice"/> and for a ship on a record. Its model (<see cref="GeneratorSpec"/>)
/// makes it better or worse; a pilot or a crew gives the orders.
/// </summary>
public sealed class GeneratorState
{
    private const float BeamFloor = 0.05f;

    public bool On { get; set; }
    /// <summary>Seconds of charging left.</summary>
    public double Charge { get; set; }
    /// <summary>How near it is to giving up against the beam, 0 to 1.</summary>
    public float Strain { get; set; }
    /// <summary>Its own frequency, 0 to 255.</summary>
    public float Tune { get; set; } = 128;

    [JsonIgnore] public bool Charging => Charge > 0;
    /// <summary>The medium here is too dense for it to thin anything.</summary>
    [JsonIgnore] public bool Locked { get; private set; }
    [JsonIgnore] public float Density { get; private set; } = 1;
    /// <summary>How much of the beam it is set to cancel, 0 to 1.</summary>
    [JsonIgnore] public float Coupling { get; private set; }

    /// <summary>How much of a beam on that frequency it would cancel when tuned there, 0 to 1.</summary>
    public static float CouplingAt(GeneratorSpec spec, float tune, float band) =>
        Mathf.Clamp((spec.TuneBand - Mathf.Abs(tune - band)) / Mathf.Max(spec.TuneBand * (1 - spec.TuneFlat), 0.01f), 0, 1);

    /// <summary>Starts it charging, if it can start.</summary>
    public bool Start(GeneratorSpec spec)
    {
        if (On || Charging || Locked)
            return false;
        Charge = spec.SpoolSeconds;
        return true;
    }

    public void Stop()
    {
        On = false;
        Charge = 0;
    }

    /// <summary>A step in the medium, with a beam adding to it on that frequency, the tuning going for <paramref name="tuneTo"/>. True if it gave out in this step.</summary>
    public bool Step(GeneratorSpec spec, float natural, float beam, byte band, float tuneTo, float dt, bool held = false)
    {
        Tune = Mathf.MoveToward(Tune, tuneTo, spec.TuneRate * dt);
        Coupling = beam > BeamFloor ? CouplingAt(spec, Tune, band) : 0;
        Density = natural + beam * (1 - Coupling * spec.Rejection);
        Locked = Density >= spec.Thinning;
        bool massed = natural >= spec.Thinning;
        if (held)
            On = false;
        if (Charging)
        {
            Charge -= dt;
            if (Locked || held)
                Charge = 0;
            else if (Charge <= 0)
                On = true;
        }
        Strain = On && Locked && !massed
            ? Strain + dt / Mathf.Max(spec.HoldSeconds, 0.01f)
            : Mathf.Max(0, Strain - dt / Mathf.Max(2 * spec.HoldSeconds, 0.01f));
        if (On && (massed || Strain >= 1))
        {
            On = false;
            Strain = 0;
            return true;
        }
        return false;
    }
}
