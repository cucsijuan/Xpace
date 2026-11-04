using Godot;

namespace Xpace.Space;

/// <summary>Turns its node for show, about its own axis. Only the look: put what collides elsewhere.</summary>
public partial class SlowSpin : Node3D
{
    [Export] public Vector3 Axis = Vector3.Up;
    [Export] public float DegreesPerSecond = 6f;

    public override void _Process(double delta) => RotateObjectLocal(Axis.Normalized(), Mathf.DegToRad(DegreesPerSecond) * (float)delta);
}
