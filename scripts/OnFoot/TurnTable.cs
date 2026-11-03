using Godot;

namespace Xpace.OnFoot;

/// <summary>
/// Turns what it holds slowly round its Y axis, as on a showroom's turntable (the ship in a fitting screen); a drag
/// turns it by hand, and it goes on turning by itself a few seconds after.
/// </summary>
public partial class TurnTable : Node3D
{
    [Export] public float DegreesPerSecond = 10f;
    /// <summary>Degrees a pixel of drag turns it.</summary>
    [Export] public float DegreesPerPixel = 0.4f;
    [Export] public double RestSeconds = 3;

    private double _held;

    public override void _Process(double delta)
    {
        _held -= delta;
        if (_held <= 0)
            RotateY(Mathf.DegToRad(DegreesPerSecond) * (float)delta);
    }

    /// <summary>Turns it by a drag of <paramref name="pixels"/> across.</summary>
    public void Drag(float pixels)
    {
        RotateY(Mathf.DegToRad(pixels * DegreesPerPixel));
        _held = RestSeconds;
    }
}
