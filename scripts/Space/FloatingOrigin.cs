using Godot;

namespace Xpace.Space;

/// <summary>
/// Something with too many world positions to keep as <see cref="Spot"/>s, or that draws them every frame (rounds,
/// trails, dust). Anything else keeps spots and needs no telling.
/// </summary>
public interface IOriginShift
{
    /// <summary>The world moved by -offset: move what you keep the same way.</summary>
    void ShiftOrigin(Vector3 offset);
}

/// <summary>
/// Keeps the ship near the world's origin. Single-precision floats lose
/// millimetres tens of kilometres out, enough to make the cockpit shake;
/// so when the ship gets <see cref="Threshold"/> metres away, every 3D node
/// under this one moves back by the ship's position. Places kept for later are
/// <see cref="Spot"/>s, which stay right by themselves; the few nodes that can't
/// use them (<see cref="IOriginShift"/>) are told.
/// Velocities do not change: nothing feels it.
/// </summary>
public partial class FloatingOrigin : Node3D
{
    [Export] public NodePath ShipPath;
    [Export] public float Threshold = 1000f;

    /// <summary>Where the current origin is in the sector: the sum of every shift, in double precision.</summary>
    public SectorPoint Origin { get; private set; }

    private Node3D _ship;

    /// <summary>The ship it keeps near the origin (null: none, as in a sector the player is not in).</summary>
    public Node3D Ship
    {
        get => _ship;
        set => _ship = value;
    }

    public override void _Ready()
    {
        _ship ??= ShipPath == null || ShipPath.IsEmpty ? null : GetNodeOrNull<Node3D>(ShipPath);
        ProcessPhysicsPriority = 100;           // after everything has moved this step
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_ship == null || _ship.GlobalPosition.Length() < Threshold)
            return;
        Shift(_ship.GlobalPosition);
    }

    /// <summary>The floating origin a node is under (its sector's root), or null.</summary>
    public static FloatingOrigin Of(Node node)
    {
        for (Node n = node; n != null; n = n.GetParent())
            if (n is FloatingOrigin origin)
                return origin;
        return null;
    }

    public void Shift(Vector3 offset)
    {
        Origin += offset;
        foreach (Node child in GetChildren())
            if (child is Node3D node && !node.TopLevel)
                node.GlobalPosition -= offset;
        foreach (Node node in FindChildren("*", "", true, false))
            if (node is IOriginShift shifting)
                shifting.ShiftOrigin(offset);
    }
}
