using Godot;

namespace Xpace.Cockpit;

/// <summary>Where the mouse stick is: a ring with a dot, in the middle of the screen.</summary>
public partial class MouseWidget : Control
{
    [Export] public float Radius = 48f;
    [Export] public Color Ring = new(0.6f, 0.9f, 0.8f, 0.35f);
    [Export] public Color Dot = new(0.7f, 1f, 0.9f, 0.9f);

    private Vector2 _stick;
    private float _deadZone;

    /// <summary>The stick's dead zone, drawn as a ring inside the widget (a share of its radius).</summary>
    public float DeadZone
    {
        get => _deadZone;
        set
        {
            if (_deadZone == value)
                return;
            _deadZone = value;
            QueueRedraw();
        }
    }

    /// <summary>-1..1 on each axis; y down.</summary>
    public Vector2 Stick
    {
        get => _stick;
        set
        {
            if (_stick == value)
                return;
            _stick = value;
            QueueRedraw();
        }
    }

    public override void _Draw()
    {
        Vector2 centre = Size / 2;
        DrawArc(centre, Radius, 0, Mathf.Tau, 48, Ring, 1.5f, true);
        if (DeadZone > 0)
            DrawArc(centre, Radius * DeadZone, 0, Mathf.Tau, 32, Ring, 1f, true);
        DrawLine(centre - new Vector2(4, 0), centre + new Vector2(4, 0), Ring, 1f);
        DrawLine(centre - new Vector2(0, 4), centre + new Vector2(0, 4), Ring, 1f);
        DrawCircle(centre + _stick * Radius, 3.5f, Dot);
    }
}
