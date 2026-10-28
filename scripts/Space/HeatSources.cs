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
}

public static class Heat
{
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
    public static float Scan(SceneTree tree, Vector3 from, Vector3 look, float halfAngle,
                             out Vector3 centre, out IHeatSource brightest)
    {
        float cos = Mathf.Cos(halfAngle), total = 0, best = 0;
        Vector3 sum = Vector3.Zero;
        brightest = null;
        foreach (Node node in tree.GetNodesInGroup("heat_sources"))
        {
            if (node is not IHeatSource source || node is Node3D n && !n.IsVisibleInTree())
                continue;
            float heat = source.HeatAt(from, out Vector3 direction);
            if (heat <= 0 || direction.Dot(look) < cos || Behind(from, node))
                continue;
            total += heat;
            sum += direction * heat;
            if (heat > best)
            {
                best = heat;
                brightest = source;
            }
        }
        centre = total > 0 ? sum.Normalized() : look;
        return total;
    }

    /// <summary>A rock or a solid between the seeker and a source (the sun, far off, is tested a long way along its direction).</summary>
    private static bool Behind(Vector3 from, Node source)
    {
        if (RockField.All.Count == 0 && Solid.All.Count == 0 || source is not Node3D n)
            return false;
        Vector3 to = n.GlobalPosition - from;
        if (to.LengthSquared() > 1e10f)
            to = to.Normalized() * 100_000f;
        return Obstacles.Blocked(from, from + to);
    }
}
