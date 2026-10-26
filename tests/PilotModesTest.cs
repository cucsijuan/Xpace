using System.Threading.Tasks;
using Godot;
using Xpace.Cockpit;
using Xpace.Menu;

namespace Xpace.Tests;

/// <summary>
/// The mouse's three modes in the game's scene, driven by input events as a player makes them:
///
///   - a tap of the middle button looks around, another flies again with the head left where it was;
///   - held, it peeks: on letting go the head is ahead again and the mouse flies; two taps put the head ahead;
///   - Alt: a tap gives the pointer and another goes back; held, the pointer lasts while it is held;
///   - clicking the computer's screen leans the head in to it, framing it; a right-click ends the typing and the head
///     goes back where it was;
///   - only flying fires; Esc opens the menu, which pauses the game alone and lets the keys go; Esc closes it and the
///     mouse goes back to its mode.
///
/// The head's turns need the mouse captured, so with no screen (headless) the test turns the head itself.
/// Run: Godot --headless --path . res://tests/pilot_modes_test.tscn
/// Exits with 0 if every check passed.
/// </summary>
public partial class PilotModesTest : Node
{
    private int _failures;
    private Pilot _pilot;
    private Camera3D _camera;

    public override async void _Ready()
    {
        var main = GD.Load<PackedScene>("res://scenes/main.tscn").Instantiate();
        AddChild(main);
        _pilot = main.GetNode<Pilot>("Ship/Pilot");
        _camera = _pilot.GetNode<Camera3D>("Camera3D");
        await Frames(10);
        Check("the pilot starts flying", _pilot.Mode == Pilot.MouseUse.Flying);

        // Look: tap in, turn, tap out: the head stays
        await Tap(MouseButton.Middle);
        Check("a tap of the middle button looks around", _pilot.Mode == Pilot.MouseUse.Looking);
        Turn();
        await Seconds(0.5);                     // more than a double tap apart
        await Tap(MouseButton.Middle);
        Check($"another flies again, the head left where it was (yaw {Mathf.RadToDeg(_camera.Rotation.Y):0} deg)",
              _pilot.Mode == Pilot.MouseUse.Flying && Mathf.Abs(_camera.Rotation.Y) > 0.1f);

        // Two taps: the head ahead
        await Seconds(0.5);
        await Tap(MouseButton.Middle);
        await Tap(MouseButton.Middle);
        Check("two taps put the head ahead, flying", _pilot.Mode == Pilot.MouseUse.Flying && _camera.Rotation.Length() < 1e-4f);

        // Held: a peek
        await Seconds(0.5);
        await Button(MouseButton.Middle, true);
        Turn();
        await Seconds(0.5);
        Check("held, it looks around", _pilot.Mode == Pilot.MouseUse.Looking);
        await Button(MouseButton.Middle, false);
        Check("let go, the head is ahead again and the mouse flies", _pilot.Mode == Pilot.MouseUse.Flying && _camera.Rotation.Length() < 1e-4f);

        // Pointer
        await Key(global::Godot.Key.Alt, true);
        await Key(global::Godot.Key.Alt, false);
        Check("a tap of Alt gives the pointer", _pilot.Mode == Pilot.MouseUse.Pointer);
        await Key(global::Godot.Key.Alt, true);
        await Key(global::Godot.Key.Alt, false);
        Check($"another goes back to flying ({_pilot.Mode})", _pilot.Mode == Pilot.MouseUse.Flying);
        await Key(global::Godot.Key.Alt, true);
        await Seconds(0.5);
        Check("held, it is the pointer", _pilot.Mode == Pilot.MouseUse.Pointer);
        await Key(global::Godot.Key.Alt, false);
        Check("let go, flying again", _pilot.Mode == Pilot.MouseUse.Flying);

        // Typing on the computer's screen, from the pointer
        await Key(global::Godot.Key.Alt, true);
        await Key(global::Godot.Key.Alt, false);
        Turn();
        Transform3D head = _camera.Transform;
        CockpitScreen screen = FindScreen(main);
        _pilot.Click(screen);
        await Seconds(1);
        Vector3 toScreen = screen.GlobalPosition - _camera.GlobalPosition;
        Vector2 view = _camera.GetViewport().GetVisibleRect().Size;
        Vector2 middle = _camera.UnprojectPosition(screen.GlobalPosition);
        Vector2 corner = _camera.UnprojectPosition(screen.ToGlobal(new Vector3(screen.Size.X / 2, screen.Size.Y / 2, 0)));
        Vector2 fill = (corner - middle).Abs() * 2 / view;
        Check($"clicking the computer's screen leans in to it ({toScreen.Length():0.00} m, filling {fill.X * 100:0}% x {fill.Y * 100:0}%)",
              _pilot.Typing && (middle - view / 2).Length() < 2 && Mathf.Max(fill.X, fill.Y) is > 0.85f and < 0.99f
              && toScreen.Normalized().Dot(-screen.GlobalBasis.Z) > 0.999f);
        await Button(MouseButton.Right, true);
        await Button(MouseButton.Right, false);
        await Seconds(1);
        Check("a right-click ends it, and the head goes back where it was",
              !_pilot.Typing && _camera.Transform.IsEqualApprox(head));
        await Key(global::Godot.Key.Alt, true);
        await Key(global::Godot.Key.Alt, false);
        await Tap(MouseButton.Middle);
        await Tap(MouseButton.Middle);

        // The menu
        await Key(global::Godot.Key.Escape, true);
        await Key(global::Godot.Key.Escape, false);
        Check($"Esc opens the menu, which pauses the game alone (paused {GetTree().Paused})", GameMenu.IsOpen && GetTree().Paused);
        Input.ActionPress(FlightInput.ThrottleUp);
        await Frames(5);
        Input.ActionRelease(FlightInput.ThrottleUp);
        Check("the keys don't fly the ship meanwhile", _pilot.GetParent<Xpace.Ship.ShipBody>().LinearVelocity.Length() < 0.01f);
        await Key(global::Godot.Key.Escape, true);
        await Key(global::Godot.Key.Escape, false);
        Check($"Esc closes it, and the game goes on (open {GameMenu.IsOpen}, paused {GetTree().Paused}, {_pilot.Mode})", !GameMenu.IsOpen && !GetTree().Paused && _pilot.Mode == Pilot.MouseUse.Flying);

        GD.Print(_failures == 0 ? "ALL PASSED" : $"{_failures} FAILED");
        GetTree().Quit(_failures == 0 ? 0 : 1);
    }

    private static CockpitScreen FindScreen(Node node)
    {
        if (node is CockpitScreen { Source: ScreenSource.Computer } screen)
            return screen;
        foreach (Node child in node.GetChildren())
            if (FindScreen(child) is CockpitScreen found)
                return found;
        return null;
    }

    /// <summary>Turns the head as a mouse would (headless there is no captured mouse to move).</summary>
    private void Turn()
    {
        if (DisplayServer.GetName() == "headless")
            _camera.Rotation = new Vector3(0.1f, 0.6f, 0);
        else
            Input.ParseInputEvent(new InputEventMouseMotion { Relative = new Vector2(-240, -40) });
    }

    private async Task Tap(MouseButton button)
    {
        await Button(button, true);
        await Button(button, false);
    }

    private async Task Button(MouseButton button, bool pressed)
    {
        Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = button, Pressed = pressed, ButtonMask = pressed ? MouseButtonMask.Middle : 0 });
        await Frames(3);
    }

    private async Task Key(Key key, bool pressed)
    {
        Input.ParseInputEvent(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = pressed });
        await Frames(3);
    }

    private async Task Seconds(double s) => await ToSignal(GetTree().CreateTimer(s), SceneTreeTimer.SignalName.Timeout);

    private async Task Frames(int n)
    {
        for (int i = 0; i < n; i++)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private void Check(string what, bool ok)
    {
        GD.Print((ok ? "ok   " : "FAIL ") + what);
        if (!ok)
            _failures++;
    }
}
