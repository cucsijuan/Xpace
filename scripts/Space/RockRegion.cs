using Godot;

namespace Xpace.Space;

/// <summary>
/// Where a <see cref="RockField"/> has rocks, and how thick they lie: a
/// density from 0 (none) to 1 (the field's most) at each point of the
/// field's own frame, in metres, in double precision (a planet's ring is
/// far bigger than a float can place to the metre). See <see cref="RockBox"/>
/// and <see cref="RockRing"/>.
/// </summary>
[GlobalClass]
public abstract partial class RockRegion : Resource
{
    /// <summary>0 to 1 at a point of the field's frame.</summary>
    public abstract double Density(double x, double y, double z);

    /// <summary>A box in the field's frame that holds every point with rocks: (min, max).</summary>
    public abstract (Vector3D Min, Vector3D Max) Bounds { get; }

    /// <summary>1 inside, falling smoothly to 0 over <paramref name="edge"/> as <paramref name="depth"/> (metres in from the boundary) goes to 0.</summary>
    protected static double Edge(double depth, double edge)
    {
        if (depth <= 0)
            return 0;
        if (edge <= 0 || depth >= edge)
            return 1;
        double t = depth / edge;
        return t * t * (3 - 2 * t);
    }
}

/// <summary>A point or vector in double precision, for a field's own frame.</summary>
public readonly struct Vector3D
{
    public readonly double X, Y, Z;

    public Vector3D(double x, double y, double z) { X = x; Y = y; Z = z; }

    public static Vector3D operator +(Vector3D a, Vector3D b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static Vector3D operator -(Vector3D a, Vector3D b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static Vector3D operator *(Vector3D a, double k) => new(a.X * k, a.Y * k, a.Z * k);
    public static implicit operator Vector3D(Vector3 v) => new(v.X, v.Y, v.Z);
    public Vector3 ToVector3() => new((float)X, (float)Y, (float)Z);
    public double this[int axis] => axis == 0 ? X : axis == 1 ? Y : Z;
}
