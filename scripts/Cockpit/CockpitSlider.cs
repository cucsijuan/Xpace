using Godot;

namespace Xpace.Cockpit;

/// <summary>
/// A slider of the cockpit model (see <see cref="CockpitControl"/> for
/// placing it and its <see cref="CockpitControl.Target"/>): dragging it
/// up and down sets the float property <see cref="Property"/> of the target
/// between <see cref="Min"/> (at the bottom) and <see cref="Max"/> (at the
/// top). The knob slides between <see cref="MinOffset"/> and
/// <see cref="MaxOffset"/>, metres from where it is modelled, in its
/// parent's frame; it follows the property if something else changes it.
/// </summary>
public partial class CockpitSlider : CockpitControl
{
    [Export] public StringName Property = "";
    [Export] public float Min = 0f, Max = 1f;
    [Export] public Vector3 MinOffset = new(0, -0.03f, 0);
    [Export] public Vector3 MaxOffset = new(0, 0.03f, 0);
    /// <summary>Mouse travel, pixels, for the whole slide.</summary>
    [Export] public float DragPixels = 250f;

    /// <summary>Where the knob is, 0 at the bottom to 1 at the top.</summary>
    public float Fraction { get; private set; }

    public override void _Process(double delta)
    {
        Node target = TargetNode;
        if (target != null && Property != null && !Property.IsEmpty && Max != Min)
            Fraction = Mathf.Clamp((target.Get(Property).AsSingle() - Min) / (Max - Min), 0, 1);
        Position = RestPosition + MinOffset.Lerp(MaxOffset, Fraction);
    }

    public override void Drag(Vector2 pixels) => Set(Fraction - pixels.Y / DragPixels);

    public override void Wheel(int notches) => Set(Fraction + notches * 0.05f);

    private void Set(float fraction)
    {
        Fraction = Mathf.Clamp(fraction, 0, 1);
        Node target = TargetNode;
        if (target != null && Property != null && !Property.IsEmpty)
            target.Set(Property, Mathf.Lerp(Min, Max, Fraction));
    }
}
