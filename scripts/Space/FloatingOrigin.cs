using Godot;

namespace Xpace.Space;

/// <summary>Something that keeps world-space positions of its own (not just its transform).</summary>
public interface IOriginShift
{
    /// <summary>The world moved by -offset: move what you keep the same way.</summary>
    void ShiftOrigin(Vector3 offset);
}

/// <summary>
/// Keeps the ship near the world's origin. Single-precision floats lose
/// millimetres tens of kilometres out, enough to make the cockpit shake;
/// so when the ship gets <see cref="Threshold"/> metres away, every 3D node
/// under this one moves back by the ship's position, and the nodes that
/// keep their own world positions (<see cref="IOriginShift"/>) are told.
/// Velocities do not change: nothing feels it.
/// </summary>
public partial class FloatingOrigin : Node3D
{
    [Export] public NodePath ShipPath;
    [Export] public float Threshold = 1000f;

    /// <summary>Where the current origin is in the universe: the sum of every shift.</summary>
    public Vector3 Offset { get; private set; }

    private Node3D _ship;

    public override void _Ready()
    {
        _ship = GetNodeOrNull<Node3D>(ShipPath);
        ProcessPhysicsPriority = 100;           // after everything has moved this step
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_ship == null || _ship.GlobalPosition.Length() < Threshold)
            return;
        Shift(_ship.GlobalPosition);
    }

    public void Shift(Vector3 offset)
    {
        Offset += offset;
        foreach (Node child in GetChildren())
            if (child is Node3D node && !node.TopLevel)
                node.GlobalPosition -= offset;
        foreach (Node node in FindChildren("*", "", true, false))
            if (node is IOriginShift shifting)
                shifting.ShiftOrigin(offset);
    }
}
