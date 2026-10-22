using Godot;
using Xpace.Ship;

namespace Xpace.Tests;

/// <summary>
/// The game scene with a script at the controls, to watch or record:
/// waits for the ship's software, turns toward the first drone, locks it
/// with T and fires. Godot --path . res://tests/combat_demo.tscn
/// </summary>
public partial class CombatDemo : Node
{
    private ShipBody _ship;
    private PilotControls _controls;
    private double _time;

    public override void _Ready()
    {
        var game = GD.Load<PackedScene>("res://scenes/main.tscn").Instantiate();
        AddChild(game);
        _ship = game.GetNode<ShipBody>("Ship");
        _ship.ControlsSource = () => _controls;
    }

    public override void _PhysicsProcess(double delta)
    {
        _time += delta;
        _controls.TargetAhead = _time is > 12 and < 12.2;
        _controls.Trigger = _time is > 14 and < 22;
    }
}
