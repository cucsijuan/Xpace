using System.Collections.Generic;
using Godot;

namespace Xpace.Space;

/// <summary>
/// Something large enough to thicken the sector's medium round it: a star, a
/// planet, a station, a rock field. A cruising ship slows by itself as it
/// nears one, since its generator can thin the veil less and less (GDD 4.3.1).
///
/// The density at a point is 1 plus (Reach / distance)² for each mass. A
/// generator of thinning T drops out at Reach / sqrt(T) from a lone mass.
/// </summary>
[GlobalClass]
public partial class MediumMass : Node3D
{
    private const float Closest = 1f;

    /// <summary>Where the density it adds is 1 (m).</summary>
    [Export] public float Reach = 46000f;

    private static readonly List<MediumMass> All = new();
    private World3D _world;

    public override void _EnterTree()
    {
        _world = GetWorld3D();
        All.Add(this);
    }

    public override void _ExitTree() => All.Remove(this);

    /// <summary>The medium's density at a point (scene coordinates) of a sector.</summary>
    public static float Density(World3D world, Vector3 point)
    {
        float density = 1;
        foreach (MediumMass mass in All)
        {
            if (mass._world != world)
                continue;
            float d2 = (mass.GlobalPosition - point).LengthSquared();
            density += mass.Reach * mass.Reach / Mathf.Max(d2, Closest);
        }
        return density;
    }
}
