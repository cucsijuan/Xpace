using Godot;
using Xpace.Computer;
using Xpace.Ship;

namespace Xpace.Cockpit;

/// <summary>
/// The seated pilot, with Elite Dangerous's keyboard and mouse controls.
///
/// Flight mode: the mouse is a virtual stick (up and down pitch, left and
/// right roll; the ring in the middle shows it). W/S move the throttle and it
/// stays where it is left, X sets it to zero, A/D yaw, Q/E and R/F strafe,
/// Z switches flight assist, T locks the target ahead and G the next one,
/// the left button fires. The middle button changes to view mode: the
/// mouse turns the head, and clicking a cockpit screen gives its computer
/// the keyboard until a right-click. Holding the right button zooms the
/// view in, to read the cockpit's screens or look at something far.
///
/// The first click takes the mouse; Esc (while not typing) gives it back.
/// F5 saves the computer, F9 loads it.
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

    [Export] public string SavePath = "user://computer.sav";

    /// <summary>The cockpit's eye point; the camera sits there.</summary>
    [Export] public Node3D Eye;

    public bool Viewing { get; private set; }

    private Camera3D _camera;
    private Label _hud;
    private MouseWidget _widget;
    private ShipBody _ship;
    private CockpitScreen _focused;
    private float _lookYaw, _lookPitch;
    private float _fov;
    private Vector2 _stick;
    private float _throttle;
    private PilotControls _controls;
    private string _notice = "";
    private double _noticeTime;
    private int _settleFrames;

    public override void _Ready()
    {
        FlightInput.Register();
        _camera = GetNode<Camera3D>("Camera3D");
        _fov = _camera.Fov;
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
            case InputEventMouseMotion motion when captured && _settleFrames == 0:
                if (Viewing)
                    Look(motion.Relative);
                else
                    MoveStick(motion.Relative);
                break;
            case InputEventMouseButton { Pressed: true } button when !captured:
                CaptureMouse();
                break;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Middle }:
                SetViewing(!Viewing);
                break;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } when Viewing:
                Focus(ScreenAhead());
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
                break;
        }
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
        if (!viewing)
        {
            _lookYaw = _lookPitch = 0;          // and the head looks ahead again
            _camera.Rotation = Vector3.Zero;
        }
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
            _throttle += Input.GetAxis(FlightInput.ThrottleDown, FlightInput.ThrottleUp) * ThrottleRate * dt;
            _throttle = Mathf.Clamp(_throttle, -1, 1);
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
        };
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
        bool zoom = Input.MouseMode == Input.MouseModeEnum.Captured && Input.IsActionPressed(FlightInput.Zoom);
        _camera.Fov = Mathf.Lerp(_camera.Fov, zoom ? ZoomFov : _fov, 1 - Mathf.Exp(-ZoomSpeed * (float)delta));
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
            : Viewing ? "Looking: click a screen to type on it, hold the right button to zoom. Middle button: fly."
            : "Flying: mouse pitch/roll, W/S throttle, X stop, A/D yaw, Q/E R/F strafe, Z assist, T/G target, click fire, right button zoom. Middle button: look.";
        if (_noticeTime > 0)
        {
            _noticeTime -= delta;
            help += "\n" + _notice;
        }
        _hud.Text = $"{state}\n{help}";
    }

    // ---------- screens and saving ----------

    private CockpitScreen ScreenAhead()
    {
        Vector3 from = _camera.GlobalPosition;
        Vector3 to = from - _camera.GlobalBasis.Z * ReachMetres;
        var query = PhysicsRayQueryParameters3D.Create(from, to);
        query.CollideWithAreas = true;
        query.CollideWithBodies = false;
        var hit = GetWorld3D().DirectSpaceState.IntersectRay(query);
        return hit.Count > 0 && hit["collider"].As<Node>()?.GetParent() is CockpitScreen screen ? screen : null;
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
