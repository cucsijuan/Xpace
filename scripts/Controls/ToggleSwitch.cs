using Godot;

namespace Xpace.Controls;

/// <summary>
/// A toggle switch: a lever with two positions (see
/// <see cref="InteractiveControl"/> for its scene and
/// <see cref="InteractiveControl.Target"/>). A click flips it: it sets the
/// bool property <see cref="Property"/> of the target, or calls
/// <see cref="Method"/> with the new position (true for on). It follows
/// the property if something else changes it. The lever (the Part) turns
/// about the scene's X through <see cref="Hinge"/>: <see cref="OnAngle"/>
/// and <see cref="OffAngle"/> degrees from where it is modelled.
/// </summary>
public partial class ToggleSwitch : InteractiveControl
{
    [Export] public StringName Property = "";
    [Export] public StringName Method = "";
    /// <summary>Where it starts if nothing says otherwise (no property).</summary>
    [Export] public bool On = true;

    [ExportGroup("Lever")]
    [Export] public Vector3 Hinge;
    [Export] public float OnAngle = 0f, OffAngle = 30f;
    /// <summary>How quickly the lever snaps over, 1/s.</summary>
    [Export] public float Response = 40f;

    private float _angle = float.NaN;

    public override void Press()
    {
        On = !On;
        Node target = TargetNode;
        if (target == null)
            return;
        if (Property != null && !Property.IsEmpty)
            target.Set(Property, On);
        else if (Method != null && !Method.IsEmpty)
            target.Call(Method, On);
    }

    public override void _Process(double delta)
    {
        Node target = TargetNode;
        if (target != null && Property != null && !Property.IsEmpty)
            On = target.Get(Property).AsBool();
        float wanted = On ? OnAngle : OffAngle;
        _angle = float.IsNaN(_angle) ? wanted : Mathf.Lerp(_angle, wanted, 1 - Mathf.Exp(-Response * (float)delta));
        var turn = new Basis(Vector3.Right, Mathf.DegToRad(_angle));
        MovePart(new Transform3D(turn, Hinge - turn * Hinge));
    }
}
