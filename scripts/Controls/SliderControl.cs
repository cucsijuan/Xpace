using Godot;

namespace Xpace.Controls;

/// <summary>
/// A slider (see <see cref="InteractiveControl"/> for its scene and
/// <see cref="InteractiveControl.Target"/>): dragging it up and down sets
/// the float property <see cref="Property"/> of the target between
/// <see cref="Min"/> (at the bottom) and <see cref="Max"/> (at the top). The
/// knob (the Part) slides along the scene's Y, from <see cref="Bottom"/> to
/// <see cref="Top"/> metres from where it is modelled; it follows the
/// property if something else changes it.
/// </summary>
public partial class SliderControl : InteractiveControl
{
    [Export] public StringName Property = "";
    [Export] public float Min = 0f, Max = 1f;
    /// <summary>The knob's travel along the scene's Y, metres from where it is modelled (Bottom negative).</summary>
    [Export] public float Bottom = -0.03f, Top = 0.03f;
    /// <summary>Mouse travel, pixels, for the whole slide.</summary>
    [Export] public float DragPixels = 250f;

    /// <summary>Where the knob is, 0 at the bottom to 1 at the top.</summary>
    public float Fraction { get; private set; }

    public override void _Process(double delta)
    {
        Node target = TargetNode;
        if (target != null && Property != null && !Property.IsEmpty && Max != Min)
            Fraction = Mathf.Clamp((target.Get(Property).AsSingle() - Min) / (Max - Min), 0, 1);
        MovePart(new Transform3D(Basis.Identity, new Vector3(0, Mathf.Lerp(Bottom, Top, Fraction), 0)));
    }

    public override void Drag(Vector2 pixels) => Slide(Fraction - pixels.Y / DragPixels);

    public override void Wheel(int notches) => Slide(Fraction + notches * 0.05f);

    private void Slide(float fraction)
    {
        Fraction = Mathf.Clamp(fraction, 0, 1);
        Node target = TargetNode;
        if (target != null && Property != null && !Property.IsEmpty)
            target.Set(Property, Mathf.Lerp(Min, Max, Fraction));
    }
}
