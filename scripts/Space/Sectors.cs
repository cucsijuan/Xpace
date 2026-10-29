using System.Collections.Generic;
using Godot;

namespace Xpace.Space;

/// <summary>
/// Sectors are worlds of their own: each sector's root (a <see cref="FloatingOrigin"/>)
/// sits in a viewport with its own World3D, so nothing in one sees anything in another.
/// </summary>
public static class Sectors
{
    /// <summary>The world (sector) a node is in; null outside the tree.</summary>
    public static World3D WorldOf(Node node) =>
        node is Node3D n3 ? (n3.IsInsideTree() ? n3.GetWorld3D() : null) : node.GetViewport()?.FindWorld3D();

    /// <summary>The nodes of a group that are in the same sector as <paramref name="from"/>.</summary>
    public static IEnumerable<Node> InGroup(Node from, StringName group)
    {
        World3D world = WorldOf(from);
        foreach (Node n in from.GetTree().GetNodesInGroup(group))
            if (WorldOf(n) == world)
                yield return n;
    }

    /// <summary>True while a node is being moved from one sector to another: it leaves the tree and enters again, whole.</summary>
    public static bool Moving { get; private set; }

    /// <summary>Takes a ship (or anything) out of its sector and puts it into another, at a place in that sector.</summary>
    public static void Move(Node3D what, FloatingOrigin to, Transform3D at)
    {
        Moving = true;
        try
        {
            what.GetParent()?.RemoveChild(what);
            to.AddChild(what);
            what.GlobalTransform = at;
        }
        finally
        {
            Moving = false;
        }
    }
}
