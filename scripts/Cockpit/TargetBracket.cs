using Godot;
using Xpace.Ship;

namespace Xpace.Cockpit;

/// <summary>
/// Corners around the target the radar has locked, and its range: what the
/// radar would put on a head-up display.
/// </summary>
public partial class TargetBracket : Control
{
    [Export] public NodePath RadarPath;
    [Export] public float Size2 = 22f;
    [Export] public Color Colour = new(1f, 0.55f, 0.3f, 0.9f);

    private RadarDevice _radar;

    public override void _Ready()
    {
        _radar = RadarPath == null || RadarPath.IsEmpty ? null : GetNodeOrNull<RadarDevice>(RadarPath);
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public override void _Process(double delta) => QueueRedraw();

    public override void _Draw()
    {
        Camera3D camera = GetViewport().GetCamera3D();
        // A target freed since the radar's last step (chaff gone) is not drawn
        if (_radar?.Locked == null || (_radar.Locked is GodotObject o && !IsInstanceValid(o))
            || camera == null || camera.IsPositionBehind(_radar.Locked.GlobalPosition))
            return;
        Vector2 c = camera.UnprojectPosition(_radar.Locked.GlobalPosition);
        float s = Size2, k = 8f;
        foreach (var (x, y) in new[] { (-1, -1), (1, -1), (-1, 1), (1, 1) })
        {
            var corner = c + new Vector2(x * s, y * s);
            DrawLine(corner, corner - new Vector2(x * k, 0), Colour, 2f);
            DrawLine(corner, corner - new Vector2(0, y * k), Colour, 2f);
        }
        float range = camera.GlobalPosition.DistanceTo(_radar.Locked.GlobalPosition);
        DrawString(ThemeDB.FallbackFont, c + new Vector2(-s, s + 16), $"{range:0} m", HorizontalAlignment.Left, -1, 13, Colour);
    }
}
