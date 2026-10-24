using Godot;

namespace Xpace.Cockpit;

/// <summary>
/// A push button of the cockpit model (see <see cref="CockpitControl"/> for
/// placing it and its <see cref="CockpitControl.Target"/>). Pressing it
/// calls <see cref="Method"/> on the target with <see cref="Arguments"/>
/// (ShipComputer: Reset, PowerOn, PowerOff; Display: PressKeyOn "RWR" 1...),
/// or flips the bool property <see cref="Toggle"/> instead. The cap sinks by
/// PressOffset (metres, in its parent's frame) for PressTime.
/// </summary>
public partial class CockpitButton : CockpitControl
{
    [Export] public StringName Method = "";
    [Export] public Godot.Collections.Array Arguments = new();
    /// <summary>A bool property to flip instead of calling a method.</summary>
    [Export] public StringName Toggle = "";

    /// <summary>How far the cap sinks, and for how long, seconds.</summary>
    [Export] public Vector3 PressOffset = new(0, -0.004f, 0);
    [Export] public float PressTime = 0.15f;

    private double _held;

    public override void Press()
    {
        _held = PressTime;
        Position = RestPosition + PressOffset;
        Node target = TargetNode;
        if (target == null)
            return;
        if (Toggle != null && !Toggle.IsEmpty)
            target.Set(Toggle, !target.Get(Toggle).AsBool());
        else if (Method != null && !Method.IsEmpty)
            target.Callv(Method, Arguments);
    }

    public override void _Process(double delta)
    {
        if (_held <= 0)
            return;
        _held -= delta;
        if (_held <= 0)
            Position = RestPosition;
    }
}
