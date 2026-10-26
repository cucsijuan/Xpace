using System;
using System.Collections.Generic;
using Godot;
using Xpace.Net;
using Xpace.Space;

namespace Xpace.Ship;

/// <summary>
/// A ship meeting rocks and solids: its hitboxes against the rocks' solid
/// spheres (<see cref="RockField"/>) and the triangles of solids such as a
/// station's modules (<see cref="Solid"/>), after <see cref="ShipMotion"/>
/// has moved it. Neither moves (they are far heavier than any ship). Where
/// a box sinks into a sphere or through a triangle the ship is pushed back
/// out, and if it was moving
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
        if (RockField.All.Count == 0 && Solid.All.Count == 0 || boxes.Length == 0)
            return 0;
        Vector3 move = s.Position - previous;
        float travel = move.Length();
        spheres.Clear();
        Vector3 middle = previous + move * 0.5f;
        float reach = bound + travel * 0.5f;
        RockField.OverlapAll(middle, reach, spheres);
        List<Solid> solids = NearSolids(middle, reach);
        if (spheres.Count == 0 && solids.Count == 0)
            return 0;

        // A fast ship: where along the step it first touched
        int checks = Math.Max(1, (int)MathF.Ceiling(travel / CheckEvery));
        Vector3 end = s.Position;
        for (int k = 1; k < checks; k++)
        {
            Vector3 at = previous + move * ((float)k / checks);
            if (Deepest(at, s.Orientation, boxes, spheres, solids, out _))
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
            if (!Deepest(s.Position, s.Orientation, boxes, spheres, solids, out Contact c))
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

    private static readonly List<Solid> _solids = new();
    private static readonly List<int> _triangles = new();

    private static List<Solid> NearSolids(Vector3 centre, float radius)
    {
        _solids.Clear();
        foreach (Solid solid in Solid.All)
            if (solid.A.Length > 0 && centre.DistanceTo(solid.GlobalPosition) < solid.Reach + radius)
                _solids.Add(solid);
        return _solids;
    }

    /// <summary>The deepest a box of the ship, placed at <paramref name="position"/>, sinks into a sphere or through a solid's triangle.</summary>
    private static bool Deepest(Vector3 position, Quaternion orientation, Hitbox[] boxes, List<RockSphere> spheres, List<Solid> solids,
                                out Contact deepest)
    {
        deepest = default;
        foreach (Solid solid in solids)
            Through(solid, position, orientation, boxes, ref deepest);
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

    /// <summary>
    /// The deepest a box goes through one of a solid's triangles. Whether
    /// they meet is told by the separating axes of a box and a triangle
    /// (the box's three, the triangle's normal, the nine crossings of their
    /// edges); the way out is along the triangle's normal, to the side the
    /// box's middle is on, as far as the box reaches past the triangle's
    /// plane. Never along an edge's crossing: that is a short way out at a
    /// small triangle's edge, sideways into the next triangle of the same
    /// wall, and the ship would stick to the wall instead of bouncing.
    /// </summary>
    private static void Through(Solid solid, Vector3 position, Quaternion orientation, Hitbox[] boxes, ref Contact deepest)
    {
        Transform3D frame = solid.Frame;
        Basis toSolid = frame.Basis.Transposed();          // the frame is not scaled
        Basis ship = toSolid * new Basis(orientation);
        Vector3 at = toSolid * (position - frame.Origin);
        foreach (Hitbox box in boxes)
        {
            Basis fromBox = box.ToBox == default ? Basis.Identity : box.ToBox.Transposed();
            Basis turn = ship * fromBox;                    // the box's axes, in the solid's frame
            Vector3 centre = at + ship * box.Centre;
            Vector3 h = box.HalfSize;
            float r = h.Length();
            solid.Near(centre - new Vector3(r, r, r), centre + new Vector3(r, r, r), _triangles);
            if (_triangles.Count == 0)
                continue;
            var t = new Triangle { U = turn.Column0, V = turn.Column1, W = turn.Column2, H = h };
            foreach (int i in _triangles)
            {
                t.A = solid.A[i] - centre;
                t.B = solid.B[i] - centre;
                t.C = solid.C[i] - centre;
                Vector3 e0 = t.B - t.A, e1 = t.C - t.B, e2 = t.A - t.C;
                if (t.Apart(t.U) || t.Apart(t.V) || t.Apart(t.W) || t.Apart(e0.Cross(e1)) ||
                    t.Apart(t.U.Cross(e0)) || t.Apart(t.U.Cross(e1)) || t.Apart(t.U.Cross(e2)) ||
                    t.Apart(t.V.Cross(e0)) || t.Apart(t.V.Cross(e1)) || t.Apart(t.V.Cross(e2)) ||
                    t.Apart(t.W.Cross(e0)) || t.Apart(t.W.Cross(e1)) || t.Apart(t.W.Cross(e2)))
                    continue;
                Vector3 n = e0.Cross(e1).Normalized();
                float side = -t.A.Dot(n);                   // the box's middle, from the plane along n
                if (side < 0)
                {
                    n = -n;
                    side = -side;
                }
                float depth = t.H.X * MathF.Abs(t.U.Dot(n)) + t.H.Y * MathF.Abs(t.V.Dot(n)) + t.H.Z * MathF.Abs(t.W.Dot(n)) - side;
                if (depth <= deepest.Depth)
                    continue;
                deepest = new Contact { Normal = frame.Basis * n, Depth = depth };
            }
        }
    }

    /// <summary>A box (axes U V W, half sizes H, centred on the origin) against a triangle A B C.</summary>
    private struct Triangle
    {
        public Vector3 U, V, W, H, A, B, C;

        /// <summary>Whether an axis parts them.</summary>
        public bool Apart(Vector3 axis)
        {
            float length2 = axis.LengthSquared();
            if (length2 < 1e-10f)
                return false;
            axis /= MathF.Sqrt(length2);
            float pa = A.Dot(axis), pb = B.Dot(axis), pc = C.Dot(axis);
            float lo = MathF.Min(pa, MathF.Min(pb, pc)), hi = MathF.Max(pa, MathF.Max(pb, pc));
            float radius = H.X * MathF.Abs(U.Dot(axis)) + H.Y * MathF.Abs(V.Dot(axis)) + H.Z * MathF.Abs(W.Dot(axis));
            return lo > radius || hi < -radius;
        }
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
