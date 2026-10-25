using System;
using Godot;

namespace Xpace.Space;

/// <summary>
/// A planet's ring about the field's origin, in its XZ plane: rocks between
/// two radii, in a layer <see cref="Thickness"/> thick, thinning out at the
/// edges; <see cref="Bands"/> make gaps and lanes along the radius.
/// </summary>
[GlobalClass]
public partial class RockRing : RockRegion
{
    [Export] public double InnerRadius = 80_000;
    [Export] public double OuterRadius = 120_000;
    [Export] public float Thickness = 1500f;
    /// <summary>Metres in from the inner and outer radii over which the rocks thin out.</summary>
    [Export] public float RadialFalloff = 4000f;
    /// <summary>Metres in from the top and bottom of the layer over which they thin out.</summary>
    [Export] public float VerticalFalloff = 500f;
    /// <summary>How many denser and thinner bands across the ring (0: even).</summary>
    [Export] public int Bands = 0;
    /// <summary>How much the bands thin it: 0 none, 1 empty gaps between them.</summary>
    [Export(PropertyHint.Range, "0,1")] public float BandDepth = 0.6f;

    public override double Density(double x, double y, double z)
    {
        double r = Math.Sqrt(x * x + z * z);
        double d = Edge(Math.Min(r - InnerRadius, OuterRadius - r), RadialFalloff) * Edge(Thickness / 2 - Math.Abs(y), VerticalFalloff);
        if (Bands > 0 && d > 0)
        {
            // A smoothed triangle wave across the ring: no sines, the same on every machine
            double u = (r - InnerRadius) / (OuterRadius - InnerRadius) * Bands;
            double f = u - Math.Floor(u);
            double tri = 1 - Math.Abs(2 * f - 1);
            d *= 1 - BandDepth * (1 - tri * tri * (3 - 2 * tri));
        }
        return d;
    }

    public override (Vector3D Min, Vector3D Max) Bounds =>
        (new Vector3D(-OuterRadius, -Thickness / 2, -OuterRadius), new Vector3D(OuterRadius, Thickness / 2, OuterRadius));
}
