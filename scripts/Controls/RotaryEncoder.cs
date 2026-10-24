using Godot;

namespace Xpace.Controls;

/// <summary>
/// A rotary encoder: a knob that turns in detents (see
/// <see cref="InteractiveControl"/> for its scene and
/// <see cref="InteractiveControl.Target"/>). Each detent, by the wheel or by
/// dragging sideways, either adds <see cref="Step"/> to the property
/// <see cref="Property"/> (held between <see cref="Min"/> and
/// <see cref="Max"/>, or going round with <see cref="Wraps"/>) or calls
/// <see cref="Method"/> with <see cref="UpArguments"/> or
/// <see cref="DownArguments"/> (a zoom knob for a screen: Display,
/// PressKeyOn, ["RWR", 1] and ["RWR", 2]). The knob (the Part) turns about
/// <see cref="Axis"/> (the scene's frame) by <see cref="DegreesPerStep"/> a
/// detent.
/// </summary>
public partial class RotaryEncoder : InteractiveControl
{
    [Export] public StringName Property = "";
    [Export] public float Step = 1f, Min = 0f, Max = 10f;
    [Export] public bool Wraps;

    [ExportGroup("Or a method per detent")]
    [Export] public StringName Method = "";
    [Export] public Godot.Collections.Array UpArguments = new();
    [Export] public Godot.Collections.Array DownArguments = new();

    [ExportGroup("Knob")]
    [Export] public Vector3 Axis = Vector3.Back;
    [Export] public float DegreesPerStep = 30f;
    /// <summary>Sideways mouse travel, pixels, for a detent.</summary>
    [Export] public float DragPixels = 40f;

    private int _detents;
    private float _dragged;

    public override void Drag(Vector2 pixels)
    {
        _dragged += pixels.X;
        while (_dragged >= DragPixels)
        {
            _dragged -= DragPixels;
            Turn(1);
        }
        while (_dragged <= -DragPixels)
        {
            _dragged += DragPixels;
            Turn(-1);
        }
    }

    public override void Release() => _dragged = 0;

    public override void Wheel(int notches)
    {
        for (int i = 0; i < Mathf.Abs(notches); i++)
            Turn(notches > 0 ? 1 : -1);
    }

    private void Turn(int way)
    {
        _detents += way;
        MovePart(new Transform3D(new Basis(Axis.Normalized(), -Mathf.DegToRad(DegreesPerStep) * _detents), Vector3.Zero));
        Node target = TargetNode;
        if (target == null)
            return;
        if (Method != null && !Method.IsEmpty)
        {
            target.Callv(Method, way > 0 ? UpArguments : DownArguments);
            return;
        }
        if (Property == null || Property.IsEmpty)
            return;
        float value = target.Get(Property).AsSingle() + way * Step;
        if (Wraps)
            value = Min + Mathf.PosMod(value - Min, Max - Min + Step);
        target.Set(Property, Mathf.Clamp(value, Min, Max));
    }
}
