using Godot;

namespace Xpace.Cockpit;

/// <summary>The dot in the middle of the view while looking around: what a click works.</summary>
public partial class LookDot : Control
{
    [Export] public float Radius = 3;
    [Export] public Color Colour = new(1, 0.85f, 0.4f, 0.9f);

    public override void _Draw()
    {
        Vector2 middle = Size / 2;
        DrawCircle(middle, Radius, Colour);
        DrawArc(middle, Radius * 3, 0, Mathf.Tau, 24, new Color(Colour, 0.5f), 1, true);
    }
}
