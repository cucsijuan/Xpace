using System;
using System.Collections.Generic;
using Godot;

namespace Xpace.Space;

/// <summary>
/// Something a heat seeker can see: a motor, a hull, a flare, the sun.
/// Anything in the "heat_sources" group with this interface counts.
/// </summary>
public interface IHeatSource
{
    /// <summary>
    /// How much of its heat reaches a seeker at <paramref name="from"/>
    /// (heat units / m²; zero if it is cold), and the way to it.
    /// </summary>
    float HeatAt(Vector3 from, out Vector3 direction);

    /// <summary>
    /// The colour of what <see cref="HeatAt"/> last gave: the share of it in the short band (about 4 µm) against the
    /// long (about 10 µm), 0 to 1 (<see cref="Heat.Share"/>). Hotter things are bluer: a flare burns far hotter than a
    /// motor's plume, a warm hull is redder than both.
    /// </summary>
    float Share => Heat.PlumeShare;
}

/// <summary>A source in a seeker's view: how much of its heat reaches it, from where, of what colour.</summary>
public struct Glimpse
{
    public IHeatSource Source;
    public float Heat;
    public Vector3 Direction;
    public float Share;
}

public static class Heat
{
    /// <summary>Temperatures (K) of what glows: a motor's plume, a warm hull or radiator, a flare's burning pellet, the sun.</summary>
    public const float PlumeKelvin = 1100f, HullKelvin = 400f, RadiatorKelvin = 650f, FlareKelvin = 2200f, SunKelvin = 5800f;

    private const float ShortMicrons = 4f, LongMicrons = 10f, SecondRadiation = 14388f;    // hc/k, µm K

    /// <summary>The short band's share of a black body's glow at that temperature (Planck's law at the two bands).</summary>
    public static float Share(float kelvin)
    {
        if (kelvin <= 1)
            return 0;
        double a = Math.Pow(ShortMicrons, -5) / (Math.Exp(SecondRadiation / (ShortMicrons * kelvin)) - 1);
        double b = Math.Pow(LongMicrons, -5) / (Math.Exp(SecondRadiation / (LongMicrons * kelvin)) - 1);
        return (float)(a / (a + b));
    }

    /// <summary>The shares of the fixed temperatures, worked out once.</summary>
    public static readonly float PlumeShare = Share(PlumeKelvin), HullShare = Share(HullKelvin), RadiatorShare = Share(RadiatorKelvin),
        SunShare = Share(SunKelvin);

    /// <summary>The short band's share of a warm hull, radiators and a plume together, each of its own heat.</summary>
    public static float Mix(float hull, float radiators, float plume)
    {
        float total = hull + radiators + plume;
        return total <= 0 ? PlumeShare : (hull * HullShare + radiators * RadiatorShare + plume * PlumeShare) / total;
    }

    /// <summary>The colour of a mixture: ln(short / long) of the summed bands (heat-weighted shares).</summary>
    public static float Index(float heat, float shortHeat) =>
        heat <= 0 ? 0 : MathF.Log(MathF.Max(shortHeat, 1e-12f) / MathF.Max(heat - shortHeat, 1e-12f));

    /// <summary>Every source within <paramref name="halfAngle"/> of <paramref name="look"/> that a seeker at <paramref name="from"/> sees, unhidden.</summary>
    public static void Look(Node where, Vector3 from, Vector3 look, float halfAngle, List<Glimpse> into)
    {
        into.Clear();
        float cos = Mathf.Cos(halfAngle);
        foreach (Node node in Sectors.InGroup(where, "heat_sources"))
        {
            if (node is not IHeatSource source || node is Node3D n && !n.IsVisibleInTree())
                continue;
            float heat = source.HeatAt(from, out Vector3 direction);
            if (heat <= 0 || direction.Dot(look) < cos || Behind(from, node))
                continue;
            into.Add(new Glimpse { Source = source, Heat = heat, Direction = direction, Share = source.Share });
        }
    }

    /// <summary>A point source: its heat thins with the square of the distance.</summary>
    public static float Point(Vector3 position, float heat, Vector3 from, out Vector3 direction)
    {
        Vector3 to = position - from;
        float d2 = to.LengthSquared();
        if (d2 < 1e-4f || heat <= 0)
        {
            direction = Vector3.Zero;
            return 0;
        }
        direction = to / Mathf.Sqrt(d2);
        return heat / d2;
    }

    /// <summary>
    /// What a seeker at <paramref name="from"/> looking along
    /// <paramref name="look"/> sees within <paramref name="halfAngle"/>
    /// (radians): the heat in all, the way to its centre (each source
    /// weighed by how bright it is) and the brightest source in view.
    /// </summary>
    public static float Scan(Node where, Vector3 from, Vector3 look, float halfAngle,
                             out Vector3 centre, out IHeatSource brightest)
    {
        Look(where, from, look, halfAngle, _scan);
        float total = 0, best = 0;
        Vector3 sum = Vector3.Zero;
        brightest = null;
        foreach (Glimpse g in _scan)
        {
            total += g.Heat;
            sum += g.Direction * g.Heat;
            if (g.Heat > best)
            {
                best = g.Heat;
                brightest = g.Source;
            }
        }
        centre = total > 0 ? sum.Normalized() : look;
        return total;
    }

    [ThreadStatic] private static List<Glimpse> _scanList;
    private static List<Glimpse> _scan => _scanList ??= new List<Glimpse>();

    /// <summary>A rock or a solid between the seeker and a source (the sun, far off, is tested a long way along its direction).</summary>
    private static bool Behind(Vector3 from, Node source)
    {
        if (RockField.All.Count == 0 && Solid.All.Count == 0 || source is not Node3D n)
            return false;
        Vector3 to = n.GlobalPosition - from;
        if (to.LengthSquared() > 1e10f)
            to = to.Normalized() * 100_000f;
        return Obstacles.Blocked(n.GetWorld3D(), from, from + to);
    }
}
