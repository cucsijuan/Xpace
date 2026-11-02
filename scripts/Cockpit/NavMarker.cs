using Godot;
using Xpace.Ship;
using Xpace.Space;

namespace Xpace.Cockpit;

/// <summary>
/// The course set on the cruise generator, on the head-up display: a diamond
/// where the contact chosen on the radio lies, with its callsign, its distance
/// and the time to get there; at the edge of the view if it is out of sight.
/// </summary>
public partial class NavMarker : Control
{
    [Export] public NodePath CruisePath;
    [Export] public NodePath ShipPath;
    [Export] public float MarkSize = 14f;
    [Export] public float EdgeMargin = 40f;
    [Export] public Color Colour = new(1f, 0.8f, 0.3f, 0.95f);

    private CruiseDevice _cruise;
    private ShipBody _ship;

    public override void _Ready()
    {
        _cruise = CruisePath == null || CruisePath.IsEmpty ? null : GetNodeOrNull<CruiseDevice>(CruisePath);
        _ship = ShipPath == null || ShipPath.IsEmpty ? null : GetNodeOrNull<ShipBody>(ShipPath);
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public override void _Process(double delta) => QueueRedraw();

    public override void _Draw()
    {
        Camera3D camera = GetViewport().GetCamera3D();
        if (_cruise?.Course is not IRadio course || camera == null || _ship == null)
            return;
        Vector3 at = course.RadioPosition;
        Vector2 middle = GetRect().Size / 2;
        bool behind = camera.IsPositionBehind(at);
        Vector2 p = camera.UnprojectPosition(at);
        if (behind)
            p = middle - (p - middle) * 1000;       // mirrored behind the eye
        Rect2 inner = GetRect().Grow(-EdgeMargin);
        if (behind || !inner.HasPoint(p))
        {
            Vector2 way = p - middle;
            float scale = Mathf.Min(inner.Size.X / 2 / Mathf.Max(Mathf.Abs(way.X), 1e-3f),
                                    inner.Size.Y / 2 / Mathf.Max(Mathf.Abs(way.Y), 1e-3f));
            p = middle + way * scale;
        }
        DrawPolyline(new[] { p + Vector2.Up * MarkSize, p + Vector2.Right * MarkSize, p + Vector2.Down * MarkSize, p + Vector2.Left * MarkSize, p + Vector2.Up * MarkSize },
                     Colour, 2.5f);
        float distance = _ship.GlobalPosition.DistanceTo(at);
        float closing = _ship.LinearVelocity.Dot((at - _ship.GlobalPosition).Normalized());
        string far = distance >= 1000 ? $"{distance / 1000:0.0} km" : $"{distance:0} m";
        string time = closing > 1 ? $"  {distance / closing:0} s" : "";
        Font font = GetThemeDefaultFont();
        DrawString(font, p + new Vector2(MarkSize + 6, 8), $"{course.Callsign}  {far}{time}", HorizontalAlignment.Left, -1, 24, Colour);
    }
}
