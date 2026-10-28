using Godot;

namespace Xpace.OnFoot;

/// <summary>
/// The actions on foot, with their default keys (physical: the same places on any layout). They are a set of their
/// own: on foot W walks, in the seat it moves the throttle, so a key can be bound in both.
/// </summary>
public static class OnFootInput
{
    public const string Forward = "walk_forward", Back = "walk_back", Left = "walk_left", Right = "walk_right";
    public const string Jump = "jump", Sprint = "sprint", Aim = "aim", Interact = "interact";

    /// <summary>In the seat of a docked ship: stand up and walk.</summary>
    public const string LeaveSeat = "leave_seat";

    public static readonly (string Action, string Name)[] Bindable =
    {
        (Forward, "Walk forward"), (Back, "Walk back"), (Left, "Walk left"), (Right, "Walk right"),
        (Sprint, "Sprint"), (Jump, "Jump"), (Aim, "Aim"), (Interact, "Use, board the ship"),
        (LeaveSeat, "Leave the seat (docked)"),
    };

    public static void Register()
    {
        Add(Forward, Key(Godot.Key.W), Joy(JoyAxis.LeftY, -1));
        Add(Back, Key(Godot.Key.S), Joy(JoyAxis.LeftY, 1));
        Add(Left, Key(Godot.Key.A), Joy(JoyAxis.LeftX, -1));
        Add(Right, Key(Godot.Key.D), Joy(JoyAxis.LeftX, 1));
        Add(Sprint, Key(Godot.Key.Shift), Button(JoyButton.LeftStick));
        Add(Jump, Key(Godot.Key.Space), Button(JoyButton.A));
        Add(Aim, new InputEventMouseButton { ButtonIndex = MouseButton.Right }, Joy(JoyAxis.TriggerLeft, 1));
        Add(Interact, Key(Godot.Key.E), Button(JoyButton.X));
        Add(LeaveSeat, Key(Godot.Key.Y), Button(JoyButton.Start));
    }

    private static void Add(string action, params InputEvent[] events)
    {
        if (InputMap.HasAction(action))
            return;
        InputMap.AddAction(action, 0.15f);
        foreach (InputEvent e in events)
            InputMap.ActionAddEvent(action, e);
    }

    private static InputEventKey Key(Key key) => new() { PhysicalKeycode = key };
    private static InputEventJoypadMotion Joy(JoyAxis axis, float value) => new() { Axis = axis, AxisValue = value };
    private static InputEventJoypadButton Button(JoyButton button) => new() { ButtonIndex = button };
}
