using System.Linq;
using Godot;

namespace Xpace.Ship;

/// <summary>
/// A piece a hull is built of, the root of a scene in scenes/ships/parts: its model, the boxes rounds and walls meet
/// (a <see cref="Space.HitArea"/> of box shapes) and what it weighs. A hull's exterior is a scene of these, placed in
/// the editor, with hardpoints among them; <see cref="Totals"/> says what they come to, spread over the boxes.
/// </summary>
public partial class ShipPart : Node3D
{
    /// <summary>What it weighs, kg.</summary>
    [Export] public float Mass = 1000f;

    /// <summary>What a hull's parts come to, in the frame of its exterior.</summary>
    public readonly record struct Sum(float Mass, Vector3 Inertia, Vector3 Centre, int Parts);

    /// <summary>
    /// The mass of the parts under an exterior, with the inertia about its origin (where a ship turns) on each axis,
    /// and where the mass centres: a part's mass is spread over its boxes by their volume. The exterior need not be in
    /// the tree: the transforms are added up from it down.
    /// </summary>
    public static Sum Totals(Node3D exterior)
    {
        float mass = 0;
        Vector3 inertia = Vector3.Zero, centre = Vector3.Zero;
        int parts = 0;
        foreach (ShipPart part in exterior.FindChildren("*", "", true, false).OfType<ShipPart>())
        {
            var boxes = part.FindChildren("*", nameof(CollisionShape3D), true, false).OfType<CollisionShape3D>()
                .Where(s => s.Shape is BoxShape3D).ToArray();
            float volume = boxes.Sum(b => Volume(b));
            parts++;
            mass += part.Mass;
            foreach (CollisionShape3D box in boxes)
            {
                Transform3D at = From(exterior, box);
                float share = part.Mass * Volume(box) / volume;
                Vector3 size = ((BoxShape3D)box.Shape).Size * at.Basis.Scale;
                var own = new Vector3(size.Y * size.Y + size.Z * size.Z, size.X * size.X + size.Z * size.Z, size.X * size.X + size.Y * size.Y) * (share / 12);
                Basis turn = at.Basis.Orthonormalized();
                Vector3 r = at.Origin;
                centre += r * share;
                for (int axis = 0; axis < 3; axis++)
                {
                    // The box's inertia about this ship axis, its axes being turned, and then how far off it is
                    float about = 0;
                    for (int i = 0; i < 3; i++)
                        about += own[i] * turn[axis][i] * turn[axis][i];
                    inertia[axis] += about + share * (r.LengthSquared() - r[axis] * r[axis]);
                }
            }
        }
        return new Sum(mass, inertia, mass > 0 ? centre / mass : Vector3.Zero, parts);
    }

    /// <summary>The box round the parts under an exterior, in its frame.</summary>
    public static Aabb Bounds(Node3D exterior)
    {
        Aabb all = default;
        bool first = true;
        foreach (ShipPart part in exterior.FindChildren("*", "", true, false).OfType<ShipPart>())
            foreach (CollisionShape3D box in part.FindChildren("*", nameof(CollisionShape3D), true, false).OfType<CollisionShape3D>().Where(s => s.Shape is BoxShape3D))
            {
                Transform3D at = From(exterior, box);
                Vector3 half = ((BoxShape3D)box.Shape).Size / 2;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 p = at * new Vector3((i & 1) != 0 ? half.X : -half.X, (i & 2) != 0 ? half.Y : -half.Y, (i & 4) != 0 ? half.Z : -half.Z);
                    all = first ? new Aabb(p, Vector3.Zero) : all.Expand(p);
                    first = false;
                }
            }
        return all;
    }

    private static float Volume(CollisionShape3D box)
    {
        Vector3 size = ((BoxShape3D)box.Shape).Size;
        return size.X * size.Y * size.Z;
    }

    private static Transform3D From(Node3D root, Node3D node)
    {
        Transform3D at = node.Transform;
        for (Node up = node.GetParent(); up != root && up != null; up = up.GetParent())
            if (up is Node3D n)
                at = n.Transform * at;
        return at;
    }
}
