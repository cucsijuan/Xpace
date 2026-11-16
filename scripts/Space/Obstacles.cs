using Godot;

namespace Xpace.Space;

/// <summary>What stops anything flying: rocks (<see cref="RockField"/>) and solids (<see cref="Solid"/>).</summary>
public static class Obstacles
{
    /// <summary>
    /// Where a step's segment first meets a rock or a solid: the share of
    /// the way along it and the normal there.
    /// </summary>
    public static bool Segment(World3D world, Vector3 from, Vector3 to, out float along, out Vector3 normal)
    {
        bool rock = RockField.SegmentAll(world, from, to, out along, out normal);
        if (Solid.SegmentAll(world, from, to, out float t, out Vector3 n) && t < along)
        {
            along = t;
            normal = n;
            return true;
        }
        return rock;
    }

    /// <summary>Whether a rock or a solid stands between two scene points: radar, radio and heat do not go through them.</summary>
    /// <remarks>Solids under <paramref name="except"/> don't count: a body whose echo comes from its own hull.</remarks>
    public static bool Blocked(World3D world, Vector3 from, Vector3 to, Node except = null) =>
        RockField.Blocked(world, from, to) || Solid.Blocked(world, from, to, except);
}
