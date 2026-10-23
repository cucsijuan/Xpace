using Godot;
using Xpace.Ship;

namespace Xpace.Cockpit;

/// <summary>What a cockpit button does.</summary>
public enum ButtonAction
{
    /// <summary>The computer's reset button: a power cycle, it boots again from the diskette.</summary>
    ResetComputer,
    /// <summary>Switches the computer on or off.</summary>
    PowerComputer,
}

/// <summary>
/// A push button of the cockpit model. Place it in a cockpit scene on the
/// button and pick what it does; in view mode, clicking it presses it. Like
/// <see cref="CockpitLever"/>, Part is the model's piece that moves, or
/// CutFrom and CutBox carve it out of a bigger mesh; it sinks by PressOffset
/// (metres, in its parent's frame) while held down. It acts on the computer
/// of the ShipBody it ends up under.
/// </summary>
public partial class CockpitButton : Node3D
{
    [Export] public ButtonAction Action;

    /// <summary>The model's piece that moves.</summary>
    [Export] public Node3D Part;

    /// <summary>Or the mesh the button is part of, and the box around its triangles.</summary>
    [Export] public MeshInstance3D CutFrom;
    [Export] public Aabb CutBox;

    /// <summary>How far the cap sinks, and for how long, seconds.</summary>
    [Export] public Vector3 PressOffset = new(0, -0.004f, 0);
    [Export] public float PressTime = 0.15f;

    /// <summary>Radius of the clickable sphere around this node, metres.</summary>
    [Export] public float Radius = 0.025f;

    private ShipBody _ship;
    private Vector3 _rest;
    private double _held;

    public override void _Ready()
    {
        _rest = Position;
        if (CutFrom != null)
            Part = MeshCut.Carve(CutFrom, CutBox, "Button");
        Part?.Reparent(this, true);
        for (Node n = GetParent(); n != null && _ship == null; n = n.GetParent())
            _ship = n as ShipBody;
        // An area, not a body: it rides along with the ship and only answers clicks
        var area = new Area3D { Monitoring = false };
        area.AddChild(new CollisionShape3D { Shape = new SphereShape3D { Radius = Radius } });
        AddChild(area);
    }

    public void Press()
    {
        _held = PressTime;
        Position = _rest + PressOffset;
        var computer = _ship?.Computer;
        if (computer == null)
            return;
        switch (Action)
        {
            case ButtonAction.ResetComputer:
                computer.Reset();
                break;
            case ButtonAction.PowerComputer:
                if (computer.Powered)
                    computer.PowerOff();
                else
                    computer.PowerOn();
                break;
        }
    }

    public override void _Process(double delta)
    {
        if (_held <= 0)
            return;
        _held -= delta;
        if (_held <= 0)
            Position = _rest;
    }
}
