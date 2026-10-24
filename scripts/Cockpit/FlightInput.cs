using Godot;

namespace Xpace.Cockpit;

/// <summary>
/// The flight actions with their default keys, as in Elite Dangerous on
/// keyboard and mouse (the mouse itself is read by <see cref="Pilot"/>).
/// Physical keys: the same places on any keyboard layout. Actions already in
/// the project's Input Map keep their keys.
/// </summary>
public static class FlightInput
{
    public const string ThrottleUp = "throttle_up", ThrottleDown = "throttle_down", ThrottleZero = "throttle_zero";
    public const string YawLeft = "yaw_left", YawRight = "yaw_right";
    public const string PitchUp = "pitch_up", PitchDown = "pitch_down";
    public const string RollLeft = "roll_left", RollRight = "roll_right";
    public const string StrafeLeft = "strafe_left", StrafeRight = "strafe_right";
    public const string StrafeUp = "strafe_up", StrafeDown = "strafe_down";
    public const string FlightAssist = "flight_assist", Boost = "boost";
    public const string FirePrimary = "fire_primary", TargetAhead = "target_ahead", NextTarget = "next_target";
    public const string NextWeapon = "next_weapon";
    public const string Zoom = "zoom";
    public const string DisplayZoomIn = "display_zoom_in", DisplayZoomOut = "display_zoom_out", DisplaySound = "display_sound";

    public static void Register()
    {
        Add(ThrottleUp, Key(Godot.Key.W), Joy(JoyAxis.TriggerRight, 1));
        Add(ThrottleDown, Key(Godot.Key.S), Joy(JoyAxis.TriggerLeft, 1));
        Add(ThrottleZero, Key(Godot.Key.X));
        Add(YawLeft, Key(Godot.Key.A), Joy(JoyAxis.RightX, -1));
        Add(YawRight, Key(Godot.Key.D), Joy(JoyAxis.RightX, 1));
        Add(PitchUp, Key(Godot.Key.Down), Joy(JoyAxis.LeftY, 1));     // stick back, nose up
        Add(PitchDown, Key(Godot.Key.Up), Joy(JoyAxis.LeftY, -1));
        Add(RollLeft, Key(Godot.Key.Left), Joy(JoyAxis.LeftX, -1));
        Add(RollRight, Key(Godot.Key.Right), Joy(JoyAxis.LeftX, 1));
        Add(StrafeLeft, Key(Godot.Key.Q), Button(JoyButton.LeftShoulder));
        Add(StrafeRight, Key(Godot.Key.E), Button(JoyButton.RightShoulder));
        Add(StrafeUp, Key(Godot.Key.R), Joy(JoyAxis.RightY, -1));
        Add(StrafeDown, Key(Godot.Key.F), Joy(JoyAxis.RightY, 1));
        Add(FlightAssist, Key(Godot.Key.Z), Button(JoyButton.Y));
        Add(Boost, Key(Godot.Key.Tab), Button(JoyButton.A));
        Add(FirePrimary, new InputEventMouseButton { ButtonIndex = MouseButton.Left }, Button(JoyButton.X));
        Add(TargetAhead, Key(Godot.Key.T), Button(JoyButton.B));
        Add(NextTarget, Key(Godot.Key.G), Button(JoyButton.DpadRight));
        Add(NextWeapon, Key(Godot.Key.N), Button(JoyButton.DpadUp));
        Add(Zoom, new InputEventMouseButton { ButtonIndex = MouseButton.Right }, Button(JoyButton.RightStick));
        Add(DisplayZoomIn, Key(Godot.Key.Bracketleft));          // the screen of interest's keys
        Add(DisplayZoomOut, Key(Godot.Key.Bracketright));
        Add(DisplaySound, Key(Godot.Key.M));
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
