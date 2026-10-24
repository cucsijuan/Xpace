using Godot;
using Xpace.Ship;

namespace Xpace.Cockpit;

/// <summary>
/// The pulse laser's gunsight on the head-up display: a circle where the
/// ship's computer says a pulse fired now meets the target (the lead point
/// it wrote to the laser), and a pipper where the pulses go at that range.
/// Nothing is worked out here: with no solution from the program there is
/// only the cross along the bore.
/// </summary>
public partial class LeadSight : Control
{
    [Export] public NodePath LaserPath;
    [Export] public float CircleRadius = 14f;
    [Export] public float BoreDistance = 1000f;         // m: where the bore cross is drawn with no solution
    [Export] public Color Colour = new(0.4f, 1f, 0.6f, 0.9f);

    private LaserDevice _laser;

    public override void _Ready()
    {
        _laser = LaserPath == null || LaserPath.IsEmpty ? null : GetNodeOrNull<LaserDevice>(LaserPath);
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public override void _Process(double delta) => QueueRedraw();

    public override void _Draw()
    {
        Camera3D camera = GetViewport().GetCamera3D();
        if (_laser == null || camera == null)
            return;
        Vector3 bore = _laser.Pipper ?? _laser.GlobalPosition - _laser.GlobalBasis.Z.Normalized() * BoreDistance;
        if (!camera.IsPositionBehind(bore))
        {
            Vector2 p = camera.UnprojectPosition(bore);
            DrawLine(p - new Vector2(6, 0), p + new Vector2(6, 0), Colour, 1.5f);
            DrawLine(p - new Vector2(0, 6), p + new Vector2(0, 6), Colour, 1.5f);
        }
        if (_laser.LeadPoint is Vector3 lead && !camera.IsPositionBehind(lead))
        {
            Vector2 c = camera.UnprojectPosition(lead);
            DrawArc(c, CircleRadius, 0, Mathf.Tau, 32, Colour, 1.5f);
            DrawCircle(c, 1.5f, Colour);
        }
    }
}
