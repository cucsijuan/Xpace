using Godot;

namespace Xpace.Space;

/// <summary>
/// A point in the sector's own frame, in double precision: what does not
/// move when the floating origin does. Scene positions are this minus the
/// floating origin's <see cref="FloatingOrigin.Origin"/>.
/// </summary>
public readonly struct SectorPoint
{
    public readonly double X, Y, Z;

    public SectorPoint(double x, double y, double z) { X = x; Y = y; Z = z; }

    public static SectorPoint operator +(SectorPoint p, Vector3 d) => new(p.X + d.X, p.Y + d.Y, p.Z + d.Z);

    /// <summary>From this point to another, as a scene vector.</summary>
    public Vector3 To(SectorPoint p) => new((float)(p.X - X), (float)(p.Y - Y), (float)(p.Z - Z));

    public override string ToString() => $"({X:0.000}, {Y:0.000}, {Z:0.000})";
}
