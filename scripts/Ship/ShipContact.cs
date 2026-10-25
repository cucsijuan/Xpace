using System;
using System.Collections.Generic;
using Godot;
using Xpace.Net;
using Xpace.Space;

namespace Xpace.Ship;

/// <summary>
/// A ship meeting rocks: its hitboxes against the rocks' solid spheres
/// (<see cref="RockField"/>), after <see cref="ShipMotion"/> has moved it.
/// The rocks do not move (they are far heavier than any ship). Where a box
/// sinks into a sphere the ship is pushed back out, and if it was moving
/// into the rock it gets an impulse through its centre: it bounces with
/// <see cref="ShipSpec.Restitution"/> of the speed it hit with and slides
/// with <see cref="ShipSpec.Friction"/>. A crash never sets the ship
/// turning (a pilot would lose all sense of where it points); its turn
/// stays as it was. The kinetic energy it loses is what the hull takes
/// (<see cref="ShipHull"/>).
///
/// Like the integrator, only + - * / and square roots: the same bits on
/// every machine (the rocks are too).
/// </summary>
public static class ShipContact
{
    /// <summary>The farthest a ship moves between two checks for contact (a fast ship is checked along its way, so it cannot jump a rock).</summary>
    public const float CheckEvery = 1.5f;
    private const int Passes = 4;

    private struct Contact
    {
        public Vector3 Normal;      // world, out of the rock into the ship
        public float Depth;
    }

    /// <summary>
    /// Resolves the rocks a ship touches after a step from <paramref name="previous"/>
    /// to <paramref name="s"/>. Returns the kinetic energy lost (joules).
    /// </summary>
    /// <param name="boxes">The ship's hitboxes, in its frame.</param>
    /// <param name="bound">The radius about the ship's origin that holds every box.</param>
    public static float Collide(ref ShipState s, Vector3 previous, Hitbox[] boxes, float bound, ShipSpec spec, List<RockSphere> spheres)
    {
        if (RockField.All.Count == 0 || boxes.Length == 0)
            return 0;
        Vector3 move = s.Position - previous;
        float travel = move.Length();
        spheres.Clear();
        RockField.OverlapAll(previous + move * 0.5f, bound + travel * 0.5f, spheres);
        if (spheres.Count == 0)
            return 0;

        // A fast ship: where along the step it first touched
        int checks = Math.Max(1, (int)MathF.Ceiling(travel / CheckEvery));
        Vector3 end = s.Position;
        for (int k = 1; k < checks; k++)
        {
            Vector3 at = previous + move * ((float)k / checks);
            if (Deepest(at, s.Orientation, boxes, spheres, out _))
            {
                end = at;
                break;
            }
        }
        s.Position = end;

        float before = Energy(s, spec);
        bool touched = false;
        for (int pass = 0; pass < Passes; pass++)
        {
            if (!Deepest(s.Position, s.Orientation, boxes, spheres, out Contact c))
                break;
            touched = true;
            s.Position += c.Normal * c.Depth;
            Push(ref s, c, spec);
        }
        return touched ? MathF.Max(0, before - Energy(s, spec)) : 0;
    }

    /// <summary>The radius about a ship's origin that holds all its boxes.</summary>
    public static float Bound(Hitbox[] boxes)
    {
        float bound = 0;
        foreach (Hitbox b in boxes)
            bound = MathF.Max(bound, b.Centre.Length() + b.HalfSize.Length());
        return bound;
    }

    /// <summary>The deepest a box of the ship, placed at <paramref name="position"/>, sinks into a sphere.</summary>
    private static bool Deepest(Vector3 position, Quaternion orientation, Hitbox[] boxes, List<RockSphere> spheres, out Contact deepest)
    {
        deepest = default;
        Quaternion toShip = orientation.Inverse();
        foreach (RockSphere sphere in spheres)
        {
            Vector3 local = toShip * (sphere.Centre - position);
            foreach (Hitbox box in boxes)
            {
                Basis toBox = box.ToBox == default ? Basis.Identity : box.ToBox;
                Vector3 p = toBox * (local - box.Centre);
                Vector3 h = box.HalfSize;
                var k = new Vector3(Math.Clamp(p.X, -h.X, h.X), Math.Clamp(p.Y, -h.Y, h.Y), Math.Clamp(p.Z, -h.Z, h.Z));
                Vector3 diff = p - k;
                float d2 = diff.LengthSquared();
                if (d2 >= sphere.Radius * sphere.Radius)
                    continue;
                Vector3 outward;        // box frame, from the box towards the sphere's centre
                float depth;
                if (d2 > 1e-12f)
                {
                    float d = MathF.Sqrt(d2);
                    outward = diff / d;
                    depth = sphere.Radius - d;
                }
                else
                {
                    // The sphere's centre is inside the box: out by the nearest face
                    int axis = 0;
                    float least = h.X - MathF.Abs(p.X);
                    for (int a = 1; a < 3; a++)
                        if (h[a] - MathF.Abs(p[a]) < least)
                        {
                            least = h[a] - MathF.Abs(p[a]);
                            axis = a;
                        }
                    outward = Vector3.Zero;
                    outward[axis] = p[axis] >= 0 ? 1 : -1;
                    depth = sphere.Radius + least;
                }
                if (depth <= deepest.Depth)
                    continue;
                Basis fromBox = toBox.Transposed();
                deepest = new Contact
                {
                    Normal = -(orientation * (fromBox * outward)),
                    Depth = depth,
                };
            }
        }
        return deepest.Depth > 0;
    }

    /// <summary>The impulse at a contact, through the ship's centre: a bounce along the normal, friction across it.</summary>
    private static void Push(ref ShipState s, Contact c, ShipSpec spec)
    {
        Vector3 n = c.Normal;
        float vn = s.Velocity.Dot(n);
        if (vn >= 0)
            return;                                 // already moving out
        float j = -(1 + spec.Restitution) * vn;     // speed change along the normal
        s.Velocity += n * j;

        Vector3 vt = s.Velocity - n * s.Velocity.Dot(n);
        float slide = vt.Length();
        if (slide > 1e-4f)
            s.Velocity -= vt / slide * MathF.Min(slide, spec.Friction * j);
    }

    private static float Energy(in ShipState s, ShipSpec spec)
    {
        Vector3 w = s.Orientation.Inverse() * s.AngularVelocity;
        Vector3 i = spec.Inertia;
        return 0.5f * spec.Mass * s.Velocity.LengthSquared() + 0.5f * (i.X * w.X * w.X + i.Y * w.Y * w.Y + i.Z * w.Z * w.Z);
    }
}
