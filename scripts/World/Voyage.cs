using System.Collections.Generic;
using Godot;
using Xpace.Ship;

namespace Xpace.World;

/// <summary>
/// How long a ship takes on a straight way through a sector, from its real numbers: at each point it flies at the top
/// its hull and generator give there, thinned by the medium near masses, with a station's speed limit near it.
/// </summary>
public static class Voyage
{
    private const float Step = 200, CruiseCap = 4096;

    /// <summary>A sector's masses (place and reach) and speed limits (place, radius and limit).</summary>
    public sealed class Space
    {
        public List<(Vector3 At, float Reach)> Masses = new();
        public List<(Vector3 At, float Radius, float Limit)> Limits = new();
    }

    /// <summary>Where a ship on that way is so many seconds out, and how fast it goes there.</summary>
    public static (Vector3 At, float Speed) Along(Space space, Vector3 a, Vector3 b, ShipSpec hull, float mass, GeneratorSpec generator, float engineThrust, double after, float cap = 0)
    {
        float length = a.DistanceTo(b);
        float top = 0;
        for (float d = 0; d < length; d += Step)
        {
            top = Top(space, a.Lerp(b, d / length), hull, mass, generator, engineThrust, cap);
            float stretch = Mathf.Min(Step, length - d);
            double leg = stretch / top;
            if (after < leg)
                return (a.Lerp(b, Mathf.Min(1, (d + stretch * (float)(after / leg)) / length)), top);
            after -= leg;
        }
        return (b, top);
    }

    public static double Seconds(Space space, Vector3 a, Vector3 b, ShipSpec hull, float mass, GeneratorSpec generator, float engineThrust, float cap = 0)
    {
        double seconds = 0;
        float length = a.DistanceTo(b);
        for (float d = 0; d < length; d += Step)
            seconds += Mathf.Min(Step, length - d) / Top(space, a.Lerp(b, d / length), hull, mass, generator, engineThrust, cap);
        return seconds;
    }

    private static float Top(Space space, Vector3 at, ShipSpec hull, float mass, GeneratorSpec generator, float engineThrust, float cap)
    {
        float density = 1;
        foreach ((Vector3 massAt, float reach) in space.Masses)
            density += reach * reach / Mathf.Max(massAt.DistanceSquaredTo(at), 1);
        float top = density < generator.Thinning
            ? Mathf.Min(hull.TopSpeed(generator.Push * hull.Mass / mass, hull.VeilDrag * density / generator.Thinning), CruiseCap)
            : hull.TopSpeed(engineThrust * hull.Mass / mass);
        foreach ((Vector3 limitAt, float radius, float limit) in space.Limits)
            if (limitAt.DistanceTo(at) < radius)
                top = Mathf.Min(top, limit);
        return cap > 0 ? Mathf.Min(top, cap) : top;
    }
}
