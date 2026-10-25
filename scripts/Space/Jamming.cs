using System;
using Godot;

namespace Xpace.Space;

/// <summary>What a jammer sends: nothing, noise, or copies of a radar's own pulses.</summary>
public enum JamMode : byte { Off, Noise, Deception }

/// <summary>
/// A noise jammer: it raises the noise a radar hears, the more the closer it
/// is and the nearer it sits to where the radar looks. Anything in the
/// "jammers" group with this interface counts.
/// </summary>
public interface IJammer
{
    Vector3 GlobalPosition { get; }

    /// <summary>
    /// Jamming-to-noise ratio it causes in a radar 1 km away at
    /// <paramref name="point"/>, looking straight at it (its antenna's gain
    /// that way included; 0 while it makes no noise). It falls with the
    /// square of the distance.
    /// </summary>
    float NoiseToward(Vector3 point);

    /// <summary>The echo it rides on (its ship's), if any: what a radar's strobe is numbered by.</summary>
    IRadarTarget Carrier { get; }
}

/// <summary>
/// A repeater jammer: it hears a radar that tracks its carrier and sends
/// the pulses back, stronger than the skin echo and later and later, so
/// the tracker's gate walks off with the copy (range gate pull-off).
/// </summary>
public interface IDeceiver : IJammer
{
    /// <summary>
    /// A radar at <paramref name="radarPosition"/> tracking the carrier asks,
    /// every step it does, what copy of the echo it hears from this jammer:
    /// where it seems to be, how it seems to move (the Doppler is copied
    /// too), and its strength as a signal-to-noise ratio at 1 km (it falls
    /// with the square of the distance, one way). False while it sends none.
    /// </summary>
    bool FalseEcho(object radar, Vector3 radarPosition, double delta, out Vector3 position, out Vector3 velocity, out float snrAt1Km);
}

/// <summary>What jammers do to radars: the noise sums, worked out the same for every kind of radar.</summary>
public static class Jamming
{
    /// <summary>
    /// The jamming-to-noise ratio a radar at <paramref name="radar"/> hears
    /// looking along <paramref name="lookingAt"/>: in full from jammers inside
    /// its beam (<paramref name="beam"/> radians off), by
    /// <paramref name="sidelobe"/> from the others. Its own ship's jammer
    /// (<paramref name="own"/> carries it) is not counted: the radar is
    /// blanked while it sends.
    /// </summary>
    public static float Noise(SceneTree tree, Vector3 radar, Vector3 lookingAt, float beam, float sidelobe, IRadarTarget own)
    {
        float jn = 0;
        foreach (Node node in tree.GetNodesInGroup("jammers"))
        {
            if (node is not IJammer j || (own != null && j.Carrier == own) || node is Node3D { Visible: false })
                continue;
            float power = j.NoiseToward(radar);
            if (power <= 0)
                continue;
            Vector3 toJammer = j.GlobalPosition - radar;
            float km = MathF.Max(toJammer.Length(), 1f) / 1000f;
            float gain = lookingAt.AngleTo(toJammer) <= beam ? 1f : sidelobe;
            jn += power / (km * km) * gain;
        }
        return jn;
    }
}

/// <summary>A copy of an echo that a repeater jammer makes in one radar's tracker.</summary>
public sealed class FalseEcho : IRadarTarget
{
    public FalseEcho(IDeceiver deceiver) => Deceiver = deceiver;

    public IDeceiver Deceiver { get; }
    /// <summary>The echo it copies: the jammer's carrier.</summary>
    public IRadarTarget Source => Deceiver.Carrier;
    public ushort RadarId => Source?.RadarId ?? 0;     // to the radar it is the same echo
    public Vector3 GlobalPosition { get; set; }
    public Vector3 Velocity { get; set; }
    /// <summary>Its signal-to-noise ratio in that radar, this step.</summary>
    public float Snr { get; set; }
    /// <summary>The jammer sends it this step.</summary>
    public bool Live { get; set; }
    public bool Alive => Live && (Source?.Alive ?? false);
    public float RadarCrossSection => 0;
    public void Paint() => Source?.Paint();
}
