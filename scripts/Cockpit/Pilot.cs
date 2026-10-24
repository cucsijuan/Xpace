using Godot;
using Xpace.Computer;
using Xpace.Controls;
using Xpace.Ship;

namespace Xpace.Cockpit;

/// <summary>
/// The seated pilot, with Elite Dangerous's keyboard and mouse controls.
///
/// Flight mode: the mouse is a virtual stick (up and down pitch, left and
/// right roll; the ring in the middle shows it). W/S move the throttle and it
/// stays where it is left (it stops at zero for a moment on the way through,
/// to find it easily), X sets it to zero, A/D yaw, Q/E and R/F strafe,
/// Z switches flight assist, T locks the target ahead and G the next one,
/// the left button fires. The middle button changes to view mode: the
/// mouse turns the head, clicking a cockpit screen gives its computer
/// the keyboard until a right-click, and clicking a button presses it. Holding the right button zooms the
/// view in, to read the cockpit's screens or look at something far; the
/// wheel zooms in steps and stays there until the middle button or the
/// wheel brings it back.
///
/// Clicking a screen also makes it the screen of interest (a thin yellow
/// frame): [ and ] change its range, M its system's sound. The cockpit's
/// controls (buttons, sliders, knobs) glow while pointed at; in view mode
/// they are worked with the middle of the view, with the mouse free with
/// the cursor: click, drag a slider or a knob, or turn it with the wheel.
///
/// The first click takes the mouse (unless it lands on a control); Esc
/// (while not typing) gives it back. F5 saves the computer, F9 loads it.
/// </summary>
public partial class Pilot : Node3D
{
    [Export] public float LookSensitivity = 0.0025f;
    [Export] public float MaxLookYaw = Mathf.Pi * 0.6f;
    [Export] public float MaxLookPitch = Mathf.Pi * 0.4f;
    [Export] public float ReachMetres = 1.5f;

    /// <summary>Field of view while zoomed, degrees, and how fast it gets there.</summary>
    [Export] public float ZoomFov = 25f;
    [Export] public float ZoomSpeed = 12f;

    /// <summary>Mouse travel, in pixels, from the centre to full stick.</summary>
    [Export] public float StickTravel = 300f;
    [Export] public float StickDeadZone = 0.05f;
    [Export] public bool InvertMouseY;

    /// <summary>How fast W/S move the throttle, full range a second.</summary>
    [Export] public float ThrottleRate = 0.6f;

    /// <summary>The throttle stops at zero on its way through; seconds of pushing to go past it.</summary>
    [Export] public float ThrottleDetent = 0.4f;

    /// <summary>
    /// Wheel zoom: each notch divides the field of view by this, from
    /// MinZoomFov (in) to MaxWheelFov (out, wider than normal); on the way
    /// through the normal view it stops there for a notch.
    /// </summary>
    [Export] public float WheelZoomStep = 1.25f;
    [Export] public float MinZoomFov = 12f;
    [Export] public float MaxWheelFov = 100f;

    [Export] public string SavePath = "user://computer.sav";

    /// <summary>The cockpit's eye point; the camera sits there.</summary>
    [Export] public Node3D Eye;

    public bool Viewing { get; private set; }

    private Camera3D _camera;
    private Label _hud;
    private MouseWidget _widget;
    private ShipBody _ship;
    private CockpitScreen _focused, _picked;
    private IInteractive _hovered, _dragging;
    private float _lookYaw, _lookPitch;
    private float _fov, _wheelFov;
    private Vector2 _stick;
    private float _throttle, _detentTime;
    private PilotControls _controls;
    private string _notice = "";
    private double _noticeTime;
    private int _settleFrames;

    public override void _Ready()
    {
        FlightInput.Register();
        _camera = GetNode<Camera3D>("Camera3D");
        _fov = _wheelFov = _camera.Fov;
        if (Eye != null)
            _camera.GlobalPosition = Eye.GlobalPosition;
        _hud = GetNodeOrNull<Label>("Hud/Label");
        _widget = GetNodeOrNull<MouseWidget>("Hud/MouseWidget");
        _ship = GetParentOrNull<ShipBody>();
        if (_ship != null)
            _ship.ControlsSource = () => _controls;
        ProcessPhysicsPriority = -1;            // the controls are ready before the ship moves
    }

    // ---------- input ----------

    public override void _Input(InputEvent e)
    {
        if (_focused != null && e is InputEventKey key)
        {
            if (!key.Echo) // the PC's keyboard repeats keys itself
                _focused.Computer?.Key(KeyMap.CodeOf(key), key.Pressed);
            GetViewport().SetInputAsHandled();
        }
    }

    public override void _UnhandledInput(InputEvent e)
    {
        bool captured = Input.MouseMode == Input.MouseModeEnum.Captured;
        switch (e)
        {
            case InputEventMouseMotion motion when _dragging != null:
                _dragging.Drag(motion.Relative);
                break;
            case InputEventMouseMotion motion when captured && _settleFrames == 0:
                if (Viewing)
                    Look(motion.Relative);
                else
                    MoveStick(motion.Relative);
                break;
            case InputEventMouseButton { Pressed: false, ButtonIndex: MouseButton.Left }:
                _dragging?.Release();
                _dragging = null;
                break;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelUp or MouseButton.WheelDown } wheel:
                int notches = wheel.ButtonIndex == MouseButton.WheelUp ? 1 : -1;
                if (_hovered is InteractiveControl control)
                    control.Wheel(notches);
                else if (captured)
                    WheelZoom(notches);
                break;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } when !captured || Viewing:
                if (!Work() && !captured)
                    CaptureMouse();
                break;
            case InputEventMouseButton { Pressed: true } when !captured:
                CaptureMouse();
                break;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Middle }:
                SetViewing(!Viewing);
                break;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right }:
                Focus(null);
                break;
            case InputEventKey { Pressed: true, Echo: false } key:
                if (key.Keycode == Key.Escape)
                    Input.MouseMode = Input.MouseModeEnum.Visible;
                else if (key.Keycode == Key.F5)
                    Save();
                else if (key.Keycode == Key.F9)
                    Load();
                else if (e.IsActionPressed(FlightInput.DisplayZoomIn))
                    Display?.PressKey(DisplayKey.ZoomIn);
                else if (e.IsActionPressed(FlightInput.DisplayZoomOut))
                    Display?.PressKey(DisplayKey.ZoomOut);
                else if (e.IsActionPressed(FlightInput.DisplaySound))
                    Display?.PressKey(DisplayKey.Sound);
                break;
        }
    }

    private DisplayDevice Display => _ship?.Device<DisplayDevice>();

    /// <summary>A click on what the pilot points at: a screen is picked (and typed on, the computer's), a control worked. False if there is nothing there.</summary>
    private bool Work()
    {
        switch (PointedAt())
        {
            case CockpitScreen screen:
                Pick(screen);
                Focus(screen.Source == ScreenSource.Computer ? screen : null);
                return true;
            case IInteractive control:
                control.Press();
                _dragging = control;
                return true;
            default:
                return false;
        }
    }

    private void Pick(CockpitScreen screen)
    {
        if (_picked != null)
            _picked.Selected = false;
        _picked = screen;
        screen.Selected = true;
        Display?.Select(screen.Source == ScreenSource.Display ? screen.DisplayName : "");
    }

    public override void _Notification(int what)
    {
        if (what == NotificationApplicationFocusOut)
            _focused?.Computer?.ReleaseKeys();
    }

    // Capturing warps the cursor to the middle of the window, and that jump
    // comes back as motion: the first frames after it are ignored.
    private void CaptureMouse()
    {
        Input.MouseMode = Input.MouseModeEnum.Captured;
        _settleFrames = 3;
    }

    private void SetViewing(bool viewing)
    {
        Viewing = viewing;
        _stick = Vector2.Zero;                  // the stick centres when you let go of it
        _wheelFov = _fov;                       // and the wheel's zoom goes back to normal
        if (!viewing)
        {
            _lookYaw = _lookPitch = 0;          // and the head looks ahead again
            _camera.Rotation = Vector3.Zero;
        }
    }

    // A notch of the wheel: in (up) or out, with a stop at the normal view
    private void WheelZoom(int notches)
    {
        float next = notches > 0 ? _wheelFov / WheelZoomStep : _wheelFov * WheelZoomStep;
        bool crosses = (_wheelFov - _fov) * (next - _fov) < 0;
        _wheelFov = crosses ? _fov : Mathf.Clamp(next, MinZoomFov, MaxWheelFov);
    }

    private void Look(Vector2 relative)
    {
        float sensitivity = LookSensitivity * _camera.Fov / _fov;   // slower when zoomed in
        _lookYaw = Mathf.Clamp(_lookYaw - relative.X * sensitivity, -MaxLookYaw, MaxLookYaw);
        _lookPitch = Mathf.Clamp(_lookPitch - relative.Y * sensitivity, -MaxLookPitch, MaxLookPitch);
        _camera.Rotation = new Vector3(_lookPitch, _lookYaw, 0);
    }

    private void MoveStick(Vector2 relative)
    {
        _stick += relative / StickTravel;
        if (_stick.Length() > 1)
            _stick = _stick.Normalized();
    }

    // ---------- the controls, every physics step ----------

    public override void _PhysicsProcess(double delta)
    {
        bool keys = _focused == null;           // typing: the keys are the computer's
        float dt = (float)delta;
        if (keys)
        {
            if (Input.IsActionPressed(FlightInput.ThrottleZero))
                _throttle = 0;
            MoveThrottle(Input.GetAxis(FlightInput.ThrottleDown, FlightInput.ThrottleUp), dt);
        }

        Vector2 stick = Viewing ? Vector2.Zero : DeadZone(_stick);
        float mousePitch = InvertMouseY ? stick.Y : -stick.Y;
        _controls = new PilotControls
        {
            Pitch = Mathf.Clamp(mousePitch + (keys ? Input.GetAxis(FlightInput.PitchDown, FlightInput.PitchUp) : 0), -1, 1),
            Roll = Mathf.Clamp(stick.X + (keys ? Input.GetAxis(FlightInput.RollLeft, FlightInput.RollRight) : 0), -1, 1),
            Yaw = keys ? Input.GetAxis(FlightInput.YawLeft, FlightInput.YawRight) : 0,
            Right = keys ? Input.GetAxis(FlightInput.StrafeLeft, FlightInput.StrafeRight) : 0,
            Up = keys ? Input.GetAxis(FlightInput.StrafeDown, FlightInput.StrafeUp) : 0,
            Throttle = _throttle,
            AssistButton = keys && Input.IsActionPressed(FlightInput.FlightAssist),
            BoostButton = keys && Input.IsActionPressed(FlightInput.Boost),
            Trigger = !Viewing && Input.MouseMode == Input.MouseModeEnum.Captured && Input.IsActionPressed(FlightInput.FirePrimary),
            TargetAhead = keys && Input.IsActionPressed(FlightInput.TargetAhead),
            NextTarget = keys && Input.IsActionPressed(FlightInput.NextTarget),
            NextWeapon = keys && Input.IsActionPressed(FlightInput.NextWeapon),
            Chaff = keys && Input.IsActionPressed(FlightInput.Chaff),
            Flare = keys && Input.IsActionPressed(FlightInput.Flare),
        };
    }

    // The throttle has a detent at zero: passing through it stops there, and
    // it only leaves zero after ThrottleDetent seconds of pushing.
    private void MoveThrottle(float input, float dt)
    {
        if (input == 0)
        {
            _detentTime = 0;
            return;
        }
        float next = Mathf.Clamp(_throttle + input * ThrottleRate * dt, -1, 1);
        if (_throttle == 0)
        {
            _detentTime += dt;
            if (_detentTime < ThrottleDetent)
                next = 0;
        }
        else if (Mathf.Sign(next) != Mathf.Sign(_throttle))
        {
            next = 0;
            _detentTime = 0;
        }
        _throttle = next;
    }

    private Vector2 DeadZone(Vector2 v)
    {
        float length = v.Length();
        if (length <= StickDeadZone)
            return Vector2.Zero;
        return v / length * ((length - StickDeadZone) / (1 - StickDeadZone));
    }

    // ---------- the HUD ----------

    public override void _Process(double delta)
    {
        if (_settleFrames > 0)
            _settleFrames--;
        IInteractive hovered = _dragging ?? PointedAt() as IInteractive;
        if (hovered != _hovered)
        {
            _hovered?.Hover(false);
            hovered?.Hover(true);
            _hovered = hovered;
        }
        bool zoom = Input.MouseMode == Input.MouseModeEnum.Captured && Input.IsActionPressed(FlightInput.Zoom);
        float fov = zoom ? Mathf.Min(ZoomFov, _wheelFov) : _wheelFov;
        _camera.Fov = Mathf.Lerp(_camera.Fov, fov, 1 -Mathf.Exp(-ZoomSpeed * (float)delta));
        if (_widget != null)
        {
            _widget.Visible = !Viewing;
            _widget.Stick = _stick;
        }
        if (_hud == null)
            return;

        ShipComputer computer = FindComputer();
        string state = computer == null ? "" :
            computer.Error != "" ? $"Computer: {computer.Error}" :
            $"Computer: {(computer.Powered ? "on" : "off")}{(computer.DriveLight ? ", drive A:" : "")}";
        if (_ship != null)
        {
            string mode = _ship.DirectMode ? "DIRECT" : _ship.AssistOn ? "ASSIST ON" : "ASSIST OFF";
            state += $"   Throttle {_throttle * 100:+0;-0;0}%   {mode}   {_ship.LinearVelocity.Length():0} m/s";
        }
        string help = Input.MouseMode != Input.MouseModeEnum.Captured ? "Click to take the mouse."
            : _focused != null ? "Typing on the computer. Right-click to stop."
            : Viewing ? "Looking: click a screen to pick it (the computer's to type), work buttons and sliders; [ ] range, M sound on the picked screen; wheel or right button zoom. Middle button: fly."
            : "Flying: mouse pitch/roll, W/S throttle, X stop, A/D yaw, Q/E R/F strafe, Z assist, T/G target, click fire, [ ] M picked screen, wheel or right button zoom. Middle button: look.";
        if (_noticeTime > 0)
        {
            _noticeTime -= delta;
            help += "\n" + _notice;
        }
        _hud.Text = $"{state}\n{help}";
    }

    // ---------- screens, buttons and saving ----------

    // The screen or control the pilot points at, within reach: the middle of
    // the view while looking around, the cursor while the mouse is free.
    private Node PointedAt()
    {
        if (Input.MouseMode != Input.MouseModeEnum.Captured)
        {
            Vector2 cursor = GetViewport().GetMousePosition();
            return Ray(_camera.ProjectRayOrigin(cursor), _camera.ProjectRayNormal(cursor));
        }
        return Viewing ? Ray(_camera.GlobalPosition, -_camera.GlobalBasis.Z) : null;
    }

    private Node Ray(Vector3 from, Vector3 direction)
    {
        Vector3 to = from + direction * ReachMetres;
        var query = PhysicsRayQueryParameters3D.Create(from, to);
        query.CollideWithAreas = true;
        query.CollideWithBodies = false;
        var hit = GetWorld3D().DirectSpaceState.IntersectRay(query);
        return hit.Count > 0 ? hit["collider"].As<Node>()?.GetParent() : null;
    }

    private void Focus(CockpitScreen screen)
    {
        if (_focused == screen)
            return;
        _focused?.Computer?.ReleaseKeys();
        _focused = screen;
    }

    private void Save()
    {
        ShipComputer computer = FindComputer();
        if (computer == null)
            return;
        using var file = Godot.FileAccess.Open(SavePath, Godot.FileAccess.ModeFlags.Write);
        file.StoreBuffer(computer.SaveState());
        Notice("Computer saved.");
    }

    private void Load()
    {
        ShipComputer computer = FindComputer();
        if (computer == null || !Godot.FileAccess.FileExists(SavePath))
        {
            Notice("Nothing saved yet.");
            return;
        }
        Notice(computer.LoadState(Godot.FileAccess.GetFileAsBytes(SavePath)) ? "Computer loaded." : "The save does not fit this computer.");
    }

    private ShipComputer FindComputer() =>
        _focused?.Computer ?? _ship?.Computer ?? GetTree().Root.FindChild("ShipComputer", true, false) as ShipComputer;

    private void Notice(string text)
    {
        _notice = text;
        _noticeTime = 3;
    }
}
