using System;
using System.Collections.Generic;
using Godot;

namespace Xpace.Space;

/// <summary>
/// A capsule round something big (a station) that pilots' lidars keep
/// clear of, as they do rocks (<see cref="RockSensor"/>): the way round
/// is found on the capsule, not on every spar of the thing. It is not
/// solid: what stops a ship is the thing's <see cref="Solid"/>. A ship
/// that means to go in (to dock) tells its sensor to look past the
/// capsules below that thing (<see cref="RockSensor.Entering"/>).
///
/// Its axis is the node's Y; <see cref="Height"/> is the length of the
/// axis between the two caps' centres (0: a sphere).
/// </summary>
[GlobalClass]
public partial class AvoidCapsule : Node3D
{
    [Export] public float Radius = 100f;
    [Export] public float Height = 0f;

    public static readonly List<AvoidCapsule> All = new();

    public override void _EnterTree() => All.Add(this);
    public override void _ExitTree() => All.Remove(this);

    /// <summary>
    /// Where a segment (scene points) first comes within <paramref name="thickness"/>
    /// of the capsule, as a share of the way along it. A segment that starts
    /// inside meets nothing: the ship is in already.
    /// </summary>
    public bool Segment(Vector3 from, Vector3 to, out float along, float thickness = 0)
    {
        along = 1;
        Vector3 axis = GlobalBasis.Y.Normalized();
        Vector3 c = GlobalPosition;
        Vector3 p0 = c - axis * (Height / 2), p1 = c + axis * (Height / 2);
        float r = Radius + thickness;
        Vector3 d = to - from;
        if (Distance(from, p0, p1) <= r)
            return false;                           // inside already: nothing to keep clear of
        float length = d.Length();
        if (length < 1e-6f)
            return false;

        // The ray against the cylinder between the caps, then against each cap's sphere
        float best = float.MaxValue;
        Vector3 ba = p1 - p0, oa = from - p0;
        float baba = ba.Dot(ba), bard = ba.Dot(d), baoa = ba.Dot(oa), rdoa = d.Dot(oa), oaoa = oa.Dot(oa);
        float a = baba * d.Dot(d) - bard * bard;
        if (baba > 1e-6f && MathF.Abs(a) > 1e-9f)
        {
            float b = baba * rdoa - baoa * bard;
            float k = baba * oaoa - baoa * baoa - r * r * baba;
            float h = b * b - a * k;
            if (h >= 0)
            {
                float t = (-b - MathF.Sqrt(h)) / a;
                float y = baoa + t * bard;
                if (t >= 0 && y > 0 && y < baba)
                    best = t;
            }
        }
        best = MathF.Min(best, Sphere(from, d, p0, r));
        best = MathF.Min(best, Sphere(from, d, p1, r));
        if (best > 1)
            return false;
        along = best;
        return true;
    }

    private static float Sphere(Vector3 from, Vector3 d, Vector3 centre, float r)
    {
        Vector3 o = from - centre;
        float a = d.Dot(d), b = o.Dot(d), k = o.Dot(o) - r * r;
        float h = b * b - a * k;
        if (h < 0)
            return float.MaxValue;
        float t = (-b - MathF.Sqrt(h)) / a;
        return t >= 0 ? t : float.MaxValue;
    }

    private static float Distance(Vector3 p, Vector3 a, Vector3 b)
    {
        Vector3 ab = b - a;
        float l2 = ab.LengthSquared();
        float t = l2 > 1e-9f ? Math.Clamp((p - a).Dot(ab) / l2, 0, 1) : 0;
        return p.DistanceTo(a + ab * t);
    }

    /// <summary>The nearest any capsule meets a segment, leaving out the capsules below <paramref name="entering"/>.</summary>
    public static bool SegmentAll(Vector3 from, Vector3 to, out float along, float thickness, Node entering)
    {
        along = 1;
        bool hit = false;
        foreach (AvoidCapsule c in All)
            if (!(entering != null && IsInstanceValid(entering) && entering.IsAncestorOf(c)) && c.Segment(from, to, out float t, thickness) && t < along)
            {
                along = t;
                hit = true;
            }
        return hit;
    }
}
