using System;
using System.Collections.Generic;
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
    public static float Noise(Node where, Vector3 radar, Vector3 lookingAt, float beam, float sidelobe, IRadarTarget own)
    {
        var lines = new List<JamLine>();
        Lines(where, radar, own, lines);
        return Noise(lines, lookingAt, beam, sidelobe);
    }

    /// <summary>
    /// What each jammer that is sending adds at a radar: the direction to it and its noise at that distance (before the
    /// gain of the beam). It depends on the radar's place only, so a radar works it out once and asks <see cref="Noise(List{JamLine}, Vector3, float, float)"/>
    /// for each echo it looks at.
    /// </summary>
    public static void Lines(Node where, Vector3 radar, IRadarTarget own, List<JamLine> into)
    {
        foreach (JamSource source in JammerPicture.Of(where))
        {
            IJammer j = source.Jammer;
            if (!source.Sending || (own != null && j.Carrier == own) || !GodotObject.IsInstanceValid(source.Node))
                continue;
            float power = j.NoiseToward(radar);
            if (power <= 0)
                continue;
            Vector3 toJammer = source.Position - radar;
            float km = MathF.Max(toJammer.Length(), 1f) / 1000f;
            into.Add(new JamLine(toJammer, power / (km * km)));
        }
    }

    /// <summary>The jamming-to-noise ratio looking along a direction: in full from jammers inside the beam, by the sidelobe level from the rest.</summary>
    public static float Noise(List<JamLine> lines, Vector3 lookingAt, float beam, float sidelobe)
    {
        float jn = 0;
        foreach (JamLine line in lines)
            jn += line.Power * (lookingAt.AngleTo(line.To) <= beam ? 1f : sidelobe);
        return jn;
    }
}

/// <summary>A jammer's noise at one radar: the direction to the jammer and the noise it brings at that distance.</summary>
public readonly record struct JamLine(Vector3 To, float Power);

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

/// <summary>A jammer as radars meet it for a step: where it stood and whether it can send at all (not hidden).</summary>
public sealed class JamSource
{
    public IJammer Jammer;
    public Node Node;
    public Vector3 Position;
    public bool Sending;
}

/// <summary>
/// The jammers of a sector, taken once a step: every radar's every echo asks what they add to the noise, so asking each
/// jammer for its place each time cost the number of radars times echoes times jammers calls into the engine.
/// </summary>
public static class JammerPicture
{
    public static List<JamSource> Of(Node from) => Sectors.Built(from, "jammers", typeof(JammerPicture), nodes =>
    {
        var list = new List<JamSource>();
        foreach (Node node in nodes)
            if (node is IJammer j)
                list.Add(new JamSource { Jammer = j, Node = node, Position = j.GlobalPosition, Sending = node is not Node3D { Visible: false } });
        return list;
    });
}
