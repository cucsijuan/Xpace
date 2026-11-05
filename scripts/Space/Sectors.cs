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

    /// <summary>
    /// A number that changes with every physics step. Asking the engine costs a call into it, and in a battle the question
    /// comes thousands of times a step, so the count is kept here.
    /// </summary>
    public static ulong Step
    {
        get
        {
            Count();
            return _physics;
        }
    }

    private static ulong _physics = 1, _process = 1;
    private static bool _counting;

    private static void Count()
    {
        if (_counting || Engine.GetMainLoop() is not SceneTree tree)
            return;
        _counting = true;
        tree.PhysicsFrame += () => _physics++;
        tree.ProcessFrame += () => _process++;
    }

    private sealed class Members
    {
        public ulong Physics, Process, Epoch;
        public List<Node> Nodes;
        public readonly Dictionary<object, object> Built = new();     // what was made from Nodes this step (grids, pictures), by who asked
    }

    // Every device asks for the same groups several times a step, so a battle of n ships asked n x n times: the
    // answer is made once per sector and group for each physics step (and each drawn frame, for what runs there).
    // The key is the world's id, not the world: a cache must not keep a sector that was freed.
    private static readonly Dictionary<(StringName, ulong), Members> Cache = new();

    // Counts the ships moved between sectors: every list made before a move is stale
    private static ulong _epoch;

    /// <summary>
    /// The nodes of a group that are in the same sector as <paramref name="from"/>, as they stood when the step began:
    /// a node that joins the group during a step is found from the next one. The list is never changed once handed out.
    /// </summary>
    public static IEnumerable<Node> InGroup(Node from, StringName group)
    {
        World3D world = WorldOf(from);
        return world == null ? System.Array.Empty<Node>() : Live(Known(from, world, group).Nodes);
    }

    private static Members Known(Node from, World3D world, StringName group)
    {
        ulong physics = Step, process = _process;
        if (!Cache.TryGetValue((group, world.GetInstanceId()), out Members known))
            Cache[(group, world.GetInstanceId())] = known = new Members();
        if (known.Nodes == null || known.Physics != physics || known.Process != process || known.Epoch != _epoch)
        {
            var nodes = new List<Node>();
            foreach (Node n in from.GetTree().GetNodesInGroup(group))
                if (WorldOf(n) == world)
                    nodes.Add(n);
            known.Nodes = nodes;
            known.Physics = physics;
            known.Process = process;
            known.Epoch = _epoch;
            known.Built.Clear();
        }
        return known;
    }

    /// <summary>
    /// The members of a group in the sector of <paramref name="from"/> as a spatial grid, made once a step and shared by
    /// everyone who asks (see <see cref="Spatial"/>, which says which grids there are).
    /// </summary>
    public static SpatialGrid<Node> GridOf(Node from, StringName group, float cell, System.Func<Node, Vector3> where) =>
        Built(from, group, (group, cell), nodes =>
        {
            var grid = new SpatialGrid<Node>(cell);
            foreach (Node n in nodes)
                grid.Add(n, where(n));
            return grid;
        });

    /// <summary>
    /// Something made from a group's members in the sector of <paramref name="from"/> once for each step, and shared by
    /// all who ask with the same <paramref name="key"/>: a picture of the group (positions as they stood when the
    /// step's first question came). Only members alive and in the tree are handed to <paramref name="make"/>.
    /// </summary>
    public static T Built<T>(Node from, StringName group, object key, System.Func<List<Node>, T> make) where T : class
    {
        World3D world = WorldOf(from);
        if (world == null)
            return make(new List<Node>());
        Members known = Known(from, world, group);
        if (known.Built.TryGetValue(key, out object made))
            return (T)made;
        var live = new List<Node>();
        foreach (Node n in known.Nodes)
            if (GodotObject.IsInstanceValid(n) && n.IsInsideTree())
                live.Add(n);
        T result = make(live);
        known.Built[key] = result;
        return result;
    }

    // A node freed since the list was made is not handed on (one that left the tree is: the list is a step old)
    private static IEnumerable<Node> Live(List<Node> nodes)
    {
        foreach (Node n in nodes)
            if (GodotObject.IsInstanceValid(n))
                yield return n;
    }

    /// <summary>True while a node is being moved from one sector to another: it leaves the tree and enters again, whole.</summary>
    public static bool Moving { get; private set; }

    /// <summary>Takes a ship (or anything) out of its sector and puts it into another, at a place in that sector.</summary>
    public static void Move(Node3D what, FloatingOrigin to, Transform3D at)
    {
        Moving = true;
        _epoch++;
        try
        {
            what.GetParent()?.RemoveChild(what);
            to.AddChild(what);
            what.GlobalTransform = at;
        }
        finally
        {
            Moving = false;
            _epoch++;
        }
    }
}
