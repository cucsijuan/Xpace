using System;
using System.Collections.Generic;
using Godot;
using Xpace.Net;
using Xpace.Space;

namespace Xpace.Ship;

/// <summary>A ship others can run into, whoever flies it and on whichever machine.</summary>
public interface ISolidShip
{
    Vector3 GlobalPosition { get; }
    Basis GlobalBasis { get; }
    Hitbox[] SolidBoxes { get; }
    /// <summary>The radius about its origin that holds its boxes.</summary>
    float SolidReach { get; }
    Vector3 SolidVelocity { get; }
    float SolidMass { get; }
    /// <summary>It can be met now (not a hidden puppet).</summary>
    bool Solid { get; }
    /// <summary>Nothing moves it: a pad's clamps or its way hold it.</summary>
    bool Immovable { get; }
    /// <summary>This machine moves it, so a crash here moves it too. A ship flown elsewhere works out its own side.</summary>
    bool MovedHere { get; }
    /// <summary>The crash's other half: how far it is pushed, the change in its speed, and the energy its hull takes.</summary>
    void Rammed(Vector3 push, Vector3 speedChange, float joules);
}

/// <summary>
/// Ships meeting ships: the hitboxes of a ship that has just moved against those of whoever is near, along the way
/// both went this step, so a fast ship cannot go through another. Where they touch they part, and the impulse along the
/// contact goes by their masses (<see cref="ShipSpec.Restitution"/> of the speed they met with is left): a fighter
/// bounces off a freighter, which hardly notices. The energy the crash took goes to the hulls, half each, as for a crash
/// into a rock (<see cref="ShipHull"/>). A ship held by a pad or on its way doesn't move but its hull takes its half.
///
/// In a session each machine works out the ships it flies, against what it sees of the others: a ship's owner has the
/// last word on it, as for everything else about it. A hard crash counts as a blow from the faster ship
/// (<see cref="Combat.Struck"/>); a bump in a hangar doesn't.
/// </summary>
public static class ShipCollision
{
    /// <summary>The group of everything that implements <see cref="ISolidShip"/>.</summary>
    public const string Group = "solid_ships";
    /// <summary>What a crash has to take from a hull to be anyone's doing, joules.</summary>
    public const float BlowJoules = 400_000f;
    /// <summary>The farthest apart two checks along the ships' way are (m).</summary>
    private const float CheckEvery = ShipContact.CheckEvery;
    /// <summary>Slower than this along an axis counts as this: with nothing moving, the shallowest way out wins (m/s).</summary>
    private const float MinimumSpeed = 0.1f;

    /// <summary>The widest ship seen, so that a question to the grid reaches it.</summary>
    public static float MostReach { get; private set; } = 30f;

    public static void Measure(float reach) => MostReach = MathF.Max(MostReach, reach);

    /// <summary>
    /// Resolves the ships a ship touches after a step from <paramref name="previous"/> to <paramref name="s"/>. Returns
    /// the energy its own hull takes (joules).
    /// </summary>
    public static float Meet(ShipBody me, ref ShipState s, Vector3 previous, float dt, List<Node> near)
    {
        near.Clear();
        float travel = s.Position.DistanceTo(previous);
        Spatial.Grid(me, Spatial.Ships).Within(s.Position, me.SolidReach + MostReach + travel * 2 + 100f, near);
        float taken = 0;
        foreach (Node node in near)
            if (node is ISolidShip other && node != me && GodotObject.IsInstanceValid(node) && other.Solid)
                taken += Against(me, ref s, previous, dt, other);
        return taken;
    }

    private static float Against(ShipBody me, ref ShipState s, Vector3 previous, float dt, ISolidShip other)
    {
        // In the other's frame, it stands still and this ship goes from one offset to another
        Vector3 there = other.GlobalPosition;
        Vector3 from = previous - (there - other.SolidVelocity * dt), to = s.Position - there;
        float reach = me.SolidReach + other.SolidReach;
        Vector3 way = to - from;
        float length2 = way.LengthSquared();
        float nearest = length2 > 1e-9f ? Math.Clamp(-from.Dot(way) / length2, 0, 1) : 1;
        if ((from + way * nearest).LengthSquared() > reach * reach)
            return 0;

        Basis mine = new(s.Orientation), theirs = other.GlobalBasis.Orthonormalized();
        int checks = Math.Max(1, (int)MathF.Ceiling(MathF.Sqrt(length2) / CheckEvery));
        for (int k = 1; k <= checks; k++)
        {
            Vector3 offset = from + way * ((float)k / checks);
            if (offset.LengthSquared() <= reach * reach && Touching(offset, s.Velocity - other.SolidVelocity, mine, me.SolidBoxes, theirs, other.SolidBoxes, out Vector3 normal, out float depth))
            {
                s.Position = there + offset;
                return Part(me, ref s, other, normal, depth);
            }
        }
        return 0;
    }

    private static float Part(ShipBody me, ref ShipState s, ISolidShip other, Vector3 n, float depth)
    {
        bool shares = other.MovedHere && !other.Immovable;
        s.Position += n * (shares ? depth / 2 : depth);
        Vector3 push = -n * (depth / 2);
        float closing = (s.Velocity - other.SolidVelocity).Dot(n);
        if (closing >= 0)
        {
            if (shares)
                other.Rammed(push, Vector3.Zero, 0);
            return 0;
        }
        float m1 = me.SolidMass, m2 = other.SolidMass;
        float mine = other.Immovable ? 1 : m2 / (m1 + m2);        // my share of the change
        float e = me.Spec.Restitution;
        float change = -(1 + e) * closing;
        s.Velocity += n * (change * mine);
        Vector3 slide = s.Velocity - other.SolidVelocity;
        slide -= n * slide.Dot(n);
        float along = slide.Length();
        if (along > 1e-4f)
            s.Velocity -= slide / along * MathF.Min(along, me.Spec.Friction * change * mine);

        float reduced = other.Immovable ? m1 : m1 * m2 / (m1 + m2);
        float each = 0.25f * reduced * (1 - e * e) * closing * closing;
        other.Rammed(shares ? push : Vector3.Zero, shares ? -n * (change * (1 - mine)) : Vector3.Zero, each);
        if (each >= BlowJoules && other is Node3D ship)
        {
            bool mineFaster = s.Velocity.LengthSquared() + (n * change * mine).LengthSquared() >= other.SolidVelocity.LengthSquared();
            if (mineFaster)
                Combat.Struck(ship, me);
            else
                Combat.Struck(me, ship);
        }
        return each;
    }

    // The deepest two boxes sink into each other: the way out for this ship, in the world
    private static bool Touching(Vector3 offset, Vector3 moving, Basis mine, Hitbox[] myBoxes, Basis theirs, Hitbox[] theirBoxes, out Vector3 normal, out float depth)
    {
        normal = Vector3.Up;
        depth = 0;
        bool touching = false;
        foreach (Hitbox a in myBoxes)
        {
            Basis aTurn = mine * Turn(a);
            Vector3 aAt = offset + mine * a.Centre;
            float aReach = a.HalfSize.Length();
            foreach (Hitbox b in theirBoxes)
            {
                Vector3 bAt = theirs * b.Centre;
                float apart = aReach + b.HalfSize.Length();
                if ((aAt - bAt).LengthSquared() > apart * apart)
                    continue;
                if (Overlap(aAt, moving, aTurn, a.HalfSize, bAt, theirs * Turn(b), b.HalfSize, out Vector3 n, out float d) && d > depth)
                {
                    normal = n;
                    depth = d;
                    touching = true;
                }
            }
        }
        return touching;
    }

    private static Basis Turn(Hitbox box) => box.ToBox == default ? Basis.Identity : box.ToBox.Inverse();

    // Two turned boxes by separating axes: their faces and the crossings of their edges. The way out is the way it came
    // in, the axis it crossed last going as it goes: the thinnest side of a wing it went into edge on is not it
    private static bool Overlap(Vector3 aAt, Vector3 moving, Basis a, Vector3 aHalf, Vector3 bAt, Basis b, Vector3 bHalf, out Vector3 normal, out float depth)
    {
        normal = Vector3.Up;
        depth = 0;
        float since = float.MaxValue;
        Vector3 between = aAt - bAt;
        for (int i = 0; i < 3; i++)
        {
            if (!Axis(a[i], between, moving, a, aHalf, b, bHalf, ref normal, ref depth, ref since) || !Axis(b[i], between, moving, a, aHalf, b, bHalf, ref normal, ref depth, ref since))
                return false;
        }
        for (int i = 0; i < 3; i++)
            for (int j = 0; j < 3; j++)
            {
                Vector3 cross = a[i].Cross(b[j]);
                float size = cross.LengthSquared();
                if (size > 1e-6f && !Axis(cross / MathF.Sqrt(size), between, moving, a, aHalf, b, bHalf, ref normal, ref depth, ref since))
                    return false;
            }
        return true;
    }

    private static bool Axis(Vector3 axis, Vector3 between, Vector3 moving, Basis a, Vector3 aHalf, Basis b, Vector3 bHalf, ref Vector3 normal, ref float depth, ref float since)
    {
        float apart = between.Dot(axis);
        float room = Extent(axis, a, aHalf) + Extent(axis, b, bHalf) - MathF.Abs(apart);
        if (room <= 0)
            return false;
        Vector3 outward = apart >= 0 ? axis : -axis;
        float inward = MathF.Max(-moving.Dot(outward), MinimumSpeed);       // how fast it is going in along this axis
        if (room / inward < since)
        {
            since = room / inward;
            depth = room;
            normal = outward;
        }
        return true;
    }

    private static float Extent(Vector3 axis, Basis box, Vector3 half) =>
        MathF.Abs(axis.Dot(box[0])) * half.X + MathF.Abs(axis.Dot(box[1])) * half.Y + MathF.Abs(axis.Dot(box[2])) * half.Z;
}
