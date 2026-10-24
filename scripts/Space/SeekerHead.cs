using System;
using Godot;

namespace Xpace.Space;

/// <summary>
/// A heat seeker's head: a detector on a gimbal that looks at a narrow
/// cone and sees the centre of all the heat in it, each source weighed by
/// how bright it is. Tracking, the gimbal turns to keep that centre in the
/// middle, as fast as its motors go and no further than its stops. The
/// same head rides on the launcher's rail (the pilot hears it) and flies
/// in the missile.
/// </summary>
public sealed class SeekerHead
{
    public float FieldDegrees = 8f;            // the whole cone it looks at
    public float Sensitivity = 1e-6f;          // the least heat it notices
    public float SlewDegrees = 60f;            // how fast the gimbal turns, per second
    public float GimbalLimitDegrees = 50f;     // how far off the nose it can look

    /// <summary>Where it points, world axes.</summary>
    public Vector3 Look = Vector3.Forward;
    /// <summary>The heat in its view at the last look.</summary>
    public float HeatSeen { get; private set; }
    /// <summary>The brightest thing in its view at the last look (null: nothing it notices).</summary>
    public IHeatSource Sees { get; private set; }
    /// <summary>It sees enough heat to follow.</summary>
    public bool Tone => HeatSeen >= Sensitivity;

    /// <summary>Looks, and turns toward the centre of the heat it sees. False if it sees nothing to follow.</summary>
    public bool Track(SceneTree tree, Vector3 from, Vector3 nose, float dt)
    {
        Vector3 centre = Scan(tree, from);
        if (!Tone)
            return false;
        Vector3 look = Turned(centre, dt);
        if (look.AngleTo(nose) > Mathf.DegToRad(GimbalLimitDegrees))
            return false;                       // against the stops: it cannot look there
        Look = look;
        return true;
    }

    /// <summary>Turns toward a direction it is told (caged, or cued), as far as the stops allow, then looks.</summary>
    public void Point(SceneTree tree, Vector3 from, Vector3 nose, Vector3 to, float dt)
    {
        Vector3 look = Turned(to, dt);
        if (look.AngleTo(nose) <= Mathf.DegToRad(GimbalLimitDegrees))
            Look = look;
        Scan(tree, from);
    }

    private Vector3 Scan(SceneTree tree, Vector3 from)
    {
        HeatSeen = Heat.Scan(tree, from, Look, Mathf.DegToRad(FieldDegrees) / 2, out Vector3 centre, out IHeatSource brightest);
        Sees = HeatSeen >= Sensitivity ? brightest : null;
        return centre;
    }

    private Vector3 Turned(Vector3 to, float dt)
    {
        to = to.Normalized();
        float angle = Look.AngleTo(to);
        Vector3 axis = Look.Cross(to);
        if (angle < 1e-6f || axis.LengthSquared() < 1e-12f)
            return to;
        return Look.Rotated(axis.Normalized(), MathF.Min(angle, Mathf.DegToRad(SlewDegrees) * dt)).Normalized();
    }
}
