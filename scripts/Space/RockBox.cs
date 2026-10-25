using System;
using Godot;

namespace Xpace.Space;

/// <summary>A box of rocks about the field's origin, thinning out near its faces.</summary>
[GlobalClass]
public partial class RockBox : RockRegion
{
    [Export] public Vector3 Size = new(6000, 1500, 6000);
    /// <summary>Metres in from each face over which the rocks thin out to none.</summary>
    [Export] public float Falloff = 800f;

    public override double Density(double x, double y, double z)
    {
        double depth = Math.Min(Size.X / 2 - Math.Abs(x), Math.Min(Size.Y / 2 - Math.Abs(y), Size.Z / 2 - Math.Abs(z)));
        return Edge(depth, Falloff);
    }

    public override (Vector3D Min, Vector3D Max) Bounds =>
        (new Vector3D(-Size.X / 2, -Size.Y / 2, -Size.Z / 2), new Vector3D(Size.X / 2, Size.Y / 2, Size.Z / 2));
}
