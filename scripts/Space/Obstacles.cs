using Godot;

namespace Xpace.Space;

/// <summary>What stops anything flying: rocks (<see cref="RockField"/>) and solids (<see cref="Solid"/>).</summary>
public static class Obstacles
{
    /// <summary>
    /// Where a step's segment first meets a rock or a solid: the share of
    /// the way along it and the normal there.
    /// </summary>
    public static bool Segment(Vector3 from, Vector3 to, out float along, out Vector3 normal)
    {
        bool rock = RockField.SegmentAll(from, to, out along, out normal);
        if (Solid.SegmentAll(from, to, out float t, out Vector3 n) && t < along)
        {
            along = t;
            normal = n;
            return true;
        }
        return rock;
    }

    /// <summary>Whether a rock or a solid stands between two scene points: radar, radio and heat do not go through them.</summary>
    public static bool Blocked(Vector3 from, Vector3 to) => RockField.Blocked(from, to) || Solid.Blocked(from, to);
}
