using System;
using System.Collections.Generic;
using Godot;
using Xpace.Space;

namespace Xpace.Net;

/// <summary>
/// A box in a ship's own frame that rounds can hit, turned as it sits on
/// the ship (a wing's box lies along the wing). Hits are tested in plain
/// code, not by the physics engine, so the host can test them against
/// where a ship was in the past.
/// </summary>
public struct Hitbox
{
    public Vector3 Centre;
    public Vector3 HalfSize;
    /// <summary>From the ship's frame to the box's (its turn, undone); zero means not turned.</summary>
    public Basis ToBox;

    public Hitbox(Vector3 centre, Vector3 size) : this(centre, size, Basis.Identity) { }

    public Hitbox(Vector3 centre, Vector3 size, Basis turn)
    {
        Centre = centre;
        HalfSize = size / 2;
        ToBox = turn.Orthonormalized().Inverse();
    }

    /// <summary>
    /// The box shapes below a node, in its frame, each turned as its shape
    /// is. Shapes of an area or body below it that is not on the rounds'
    /// layer (<see cref="Hitboxes.RoundLayer"/>) are something else (a
    /// screen the mouse can point at) and are left out.
    /// </summary>
    public static Hitbox[] FromShapes(Node3D root)
    {
        var boxes = new List<Hitbox>();
        Transform3D toRoot = root.GlobalTransform.AffineInverse();
        foreach (Node n in root.FindChildren("*", nameof(CollisionShape3D), true, false))
            if (n is CollisionShape3D { Shape: BoxShape3D box } shape && !OtherUse(shape, root))
            {
                Transform3D t = toRoot * shape.GlobalTransform;
                Vector3 scale = t.Basis.Scale;
                boxes.Add(new Hitbox(t.Origin, box.Size * scale, t.Basis.Orthonormalized()));
            }
        return boxes.ToArray();
    }

    private static bool OtherUse(Node shape, Node root)
    {
        for (Node p = shape.GetParent(); p != null && p != root; p = p.GetParent())
            if (p is CollisionObject3D o)
                return (o.CollisionLayer & Hitboxes.RoundLayer) == 0;
        return false;
    }

    /// <summary>
    /// Whether a round moving from <paramref name="from"/> to <paramref name="to"/>
    /// during one step goes through the box of a ship moving from pose
    /// <paramref name="before"/> to pose <paramref name="after"/>. Both ends are
    /// taken into the ship's frame at their own time: the relative motion
    /// across the step counts, not just where the ship was at one instant.
    /// </summary>
    public readonly bool Swept(SectorPoint from, SectorPoint to, RemoteTrack.Pose before, RemoteTrack.Pose after)
    {
        Basis toBox = ToBox == default ? Basis.Identity : ToBox;
        Vector3 a = toBox * (before.Orientation.Inverse() * before.Position.To(from) - Centre);
        Vector3 b = toBox * (after.Orientation.Inverse() * after.Position.To(to) - Centre);
        return Segment(a, b, HalfSize);
    }

    /// <summary>Slab test: the segment a..b against the box ±half about the origin.</summary>
    public static bool Segment(Vector3 a, Vector3 b, Vector3 half)
    {
        Vector3 d = b - a;
        float enter = 0, leave = 1;
        for (int axis = 0; axis < 3; axis++)
        {
            float start = a[axis], move = d[axis], h = half[axis];
            if (MathF.Abs(move) < 1e-9f)
            {
                if (start < -h || start > h)
                    return false;
                continue;
            }
            float t0 = (-h - start) / move, t1 = (h - start) / move;
            if (t0 > t1)
                (t0, t1) = (t1, t0);
            enter = MathF.Max(enter, t0);
            leave = MathF.Min(leave, t1);
            if (enter > leave)
                return false;
        }
        return true;
    }
}
