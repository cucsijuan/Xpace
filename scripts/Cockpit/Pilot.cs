using System;
using System.Linq;
using Godot;
using Xpace.Computer;
using Xpace.Controls;
using Xpace.Menu;
using Xpace.Ship;
using Xpace.Space;

namespace Xpace.Cockpit;

/// <summary>
/// The seated pilot, with Elite Dangerous's keyboard and mouse controls, and three ways to use the mouse:
///
///   - Flying: the mouse is a virtual stick (up and down pitch, left and right roll; the ring in the middle shows it).
///     W/S move the throttle and it stays where it is left (it stops at zero for a moment on the way through, to
///     find it easily), X sets it to zero, A/D yaw, Q/E and R/F strafe, Z switches flight assist, T locks the target
///     ahead and G the next one, the left button fires.
///   - Looking (middle button): the mouse turns the head; the dot in the middle works what it is on (click a screen,
///     press a button, drag a slider or a knob). The keys still fly the ship.
///   - Pointer (Alt): the cursor is free and the head stays where it was: point at a screen or a control and work it.
///     The keys still fly the ship.
///
/// Look and Pointer work the same way: a tap stays in the mode until the next tap; held, the mode lasts while it is
/// held. Leaving Looking with a tap leaves the head where it is (fly while watching a screen); a peek (held) puts it
/// back ahead, and so does tapping Look twice, or Look ahead. Clicking the computer's screen gives it the keyboard
/// (the flight keys type, Esc too) until a right-click, and the head leans in to it: the camera moves in front of the
/// screen and frames it, and goes back to where the head was when the typing ends. Esc opens the game's menu (<see cref="GameMenu"/>).
///
/// Holding the right button zooms the view in, to read the cockpit's screens or look at something far; the wheel
/// zooms in steps and stays there until the wheel brings it back (over a control, the wheel turns it). Clicking a
/// screen also makes it the screen of interest (a thin yellow frame): [ and ] change its range, M its system's sound.
/// The bar at the bottom says which mode the mouse is in. F5 saves the computer, F9 loads it.
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

    /// <summary>Shorter than this, a press of Look or Pointer is a tap (it stays); longer, a hold (it lasts while held).</summary>
    [Export] public float HoldSeconds = 0.3f;

    /// <summary>Two taps of Look within this put the head back ahead.</summary>
    [Export] public float DoubleTapSeconds = 0.35f;

    /// <summary>Typing on a screen: how much room round it the framed view leaves (1 = edge to edge), and how long the head takes to lean in.</summary>
    [Export] public float FocusMargin = 1.08f;
    [Export] public float FocusSeconds = 0.6f;

    public enum MouseUse
    {
        Flying,
        Looking,
        Pointer,
    }

    public MouseUse Mode { get; private set; }

    public bool Viewing => Mode == MouseUse.Looking;

    /// <summary>The keyboard is the computer's.</summary>
    public bool Typing => _focused != null;

    private Camera3D _camera;
    private Label _hud;
    private ShipHull _hull;
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
    private float _stickTravel, _lookSensitivity;
    private bool _invertMouseY;
    private Label _modeBar;
    private Control _dot;
    private MouseUse _beforePointer;
    private double _lookPressed = -1, _pointerPressed = -1, _lastLookTap = -10, _modeShown;
    private MouseUse _lookFrom;
    private bool _zoomHeld;
    private float _lean;
    private Transform3D _headPose, _framed;

    public override void _Ready()
    {
        FlightInput.Register();
        _stickTravel = StickTravel;
        _lookSensitivity = LookSensitivity;
        _invertMouseY = InvertMouseY;
        if (GameSettings.Current is GameSettings settings)
        {
            Settle(settings);
            settings.Changed += Settle;
            TreeExiting += () => settings.Changed -= Settle;
        }
        _camera = GetNode<Camera3D>("Camera3D");
        _fov = _wheelFov = _camera.Fov;
        if (Eye != null)
            _camera.GlobalPosition = Eye.GlobalPosition;
        _hud = GetNodeOrNull<Label>("Hud/Label");
        _widget = GetNodeOrNull<MouseWidget>("Hud/MouseWidget");
        _modeBar = GetNodeOrNull<Label>("Hud/ModeBar");
        _dot = GetNodeOrNull<Control>("Hud/LookDot");
        GameMenu.Opened += HideBar;
        GameMenu.Closed += Resume;
        TreeExiting += () =>
        {
            GameMenu.Opened -= HideBar;
            GameMenu.Closed -= Resume;
        };
        _ship = GetParentOrNull<ShipBody>();
        if (_ship != null)
            _ship.ControlsSource = () => _controls;
        ProcessPhysicsPriority = -1;            // the controls are ready before the ship moves
    }

    /// <summary>The player's mouse settings, over the cockpit's own.</summary>
    private void Settle() => Settle(GameSettings.Current);

    private void Settle(GameSettings settings)
    {
        StickTravel = _stickTravel / Mathf.Max(settings.MouseSensitivity, 0.05f);
        LookSensitivity = _lookSensitivity * settings.LookSensitivity;
        InvertMouseY = _invertMouseY != settings.InvertMouseY;
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
        if (GameMenu.IsOpen)
            return;
        bool captured = Input.MouseMode == Input.MouseModeEnum.Captured;
        bool wantsCapture = Mode != MouseUse.Pointer;
        if (e.IsActionPressed(FlightInput.Look, false, true))
            LookPressed();
        else if (e.IsActionReleased(FlightInput.Look, true))
            LookReleased();
        else if (e.IsActionPressed(FlightInput.Pointer, false, true))
            PointerPressed();
        else if (e.IsActionReleased(FlightInput.Pointer, true))
            PointerReleased();
        else if (e.IsActionPressed(FlightInput.LookAhead, false, true))
            LookAhead();
        switch (e)
        {
            case InputEventMouseMotion motion when _dragging != null:
                _dragging.Drag(motion.Relative);
                break;
            case InputEventMouseMotion motion when captured && _settleFrames == 0:
                if (Mode == MouseUse.Looking && !Typing && _lean == 0)
                    Look(motion.Relative);
                else if (Mode == MouseUse.Flying)
                    MoveStick(motion.Relative);
                break;
            case InputEventMouseButton { Pressed: false, ButtonIndex: MouseButton.Left }:
                _dragging?.Release();
                _dragging = null;
                break;
            case InputEventMouseButton { Pressed: true } when wantsCapture && !captured:
                CaptureMouse();                 // the mouse came back (the window had lost it): this click only takes it
                break;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelUp or MouseButton.WheelDown } wheel:
                int notches = wheel.ButtonIndex == MouseButton.WheelUp ? 1 : -1;
                if (_hovered is InteractiveControl control)
                    control.Wheel(notches);
                else
                    WheelZoom(notches);
                break;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } when Mode != MouseUse.Flying:
                Work();
                break;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right } when Typing:
                Focus(null);
                _zoomHeld = true;               // this press ended the typing: it doesn't zoom too
                ShowMode();
                break;
            case InputEventKey { Pressed: true, Echo: false } key:
                if (key.Keycode == Key.Escape)
                {
                    GetViewport().SetInputAsHandled();      // or the menu takes this Esc as closing it
                    GameMenu.Open();
                }
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

    private static double Now => Time.GetTicksMsec() / 1000.0;

    // Look: a tap goes in, or back to flying with the head left where it is; a hold peeks; two taps look ahead
    private void LookPressed()
    {
        if (Now - _lastLookTap < DoubleTapSeconds)
        {
            _lastLookTap = -10;
            _lookPressed = -1;
            LookAhead();
            SetMode(MouseUse.Flying);
            return;
        }
        _lookPressed = Now;
        _lookFrom = Mode;
        SetMode(Mode == MouseUse.Looking ? MouseUse.Flying : MouseUse.Looking);
    }

    private void LookReleased()
    {
        if (_lookPressed < 0)
            return;
        bool held = Now - _lookPressed > HoldSeconds;
        _lookPressed = -1;
        if (!held)
            _lastLookTap = Now;
        else if (Mode == MouseUse.Looking)
        {
            LookAhead();                        // a peek: the head comes back
            SetMode(_lookFrom);
        }
    }

    // Pointer: a tap goes in or back to what the mouse did before; a hold lasts while held
    private void PointerPressed()
    {
        _pointerPressed = Now;
        if (Mode == MouseUse.Pointer)
            SetMode(_beforePointer);
        else
        {
            _beforePointer = Mode;
            SetMode(MouseUse.Pointer);
        }
    }

    private void PointerReleased()
    {
        if (_pointerPressed < 0)
            return;
        bool held = Now - _pointerPressed > HoldSeconds;
        _pointerPressed = -1;
        if (held && Mode == MouseUse.Pointer)
            SetMode(_beforePointer);
    }

    private void HideBar()
    {
        if (_modeBar != null)
            _modeBar.Text = "";
        if (_dot != null)
            _dot.Visible = false;
    }

    /// <summary>The menu closed: the mouse goes back to what it was doing.</summary>
    private void Resume()
    {
        if (IsInsideTree())
            SetMode(Mode);
    }

    private DisplayDevice Display => _ship?.Device<DisplayDevice>();

    /// <summary>A click on what the pilot points at: a screen is picked (and typed on, the computer's), a control worked. False if there is nothing there.</summary>
    private bool Work()
    {
        switch (PointedAt())
        {
            case CockpitScreen screen:
                Click(screen);
                return true;
            case IInteractive control:
                control.Press();
                _dragging = control;
                return true;
            default:
                return false;
        }
    }

    /// <summary>Picks a screen as a click on it does: a computer's screen takes the keyboard, and the head leans in to it.</summary>
    public void Click(CockpitScreen screen)
    {
        Pick(screen);
        Focus(screen.Source == ScreenSource.Computer ? screen : null);
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

    private void SetMode(MouseUse mode)
    {
        Mode = mode;
        _stick = Vector2.Zero;                  // the stick centres when you let go of it
        if (mode == MouseUse.Pointer)
        {
            bool wasCaptured = Input.MouseMode == Input.MouseModeEnum.Captured;
            Input.MouseMode = Input.MouseModeEnum.Visible;
            if (wasCaptured)
                Input.WarpMouse(GetViewport().GetVisibleRect().Size / 2);   // the cursor comes out where the dot was
        }
        else
            CaptureMouse();
        ShowMode();
    }

    /// <summary>The head looks ahead again.</summary>
    private void LookAhead()
    {
        _lookYaw = _lookPitch = 0;
        _camera.Rotation = Vector3.Zero;
    }

    private void ShowMode() => _modeShown = Now;

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
        bool keys = _focused == null && !GameMenu.IsOpen;      // typing: the keys are the computer's
        float dt = (float)delta;
        if (keys)
        {
            if (Input.IsActionPressed(FlightInput.ThrottleZero))
                _throttle = 0;
            MoveThrottle(Input.GetAxis(FlightInput.ThrottleDown, FlightInput.ThrottleUp), dt);
        }

        bool flying = Mode == MouseUse.Flying && Input.MouseMode == Input.MouseModeEnum.Captured && !GameMenu.IsOpen;
        Vector2 stick = flying ? DeadZone(_stick) : Vector2.Zero;
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
            SafeButton = keys && Input.IsActionPressed(FlightInput.SafeTurns),
            BoostButton = keys && Input.IsActionPressed(FlightInput.Boost),
            Trigger = flying && Input.IsActionPressed(FlightInput.FirePrimary),
            TargetAhead = keys && Input.IsActionPressed(FlightInput.TargetAhead),
            NextTarget = keys && Input.IsActionPressed(FlightInput.NextTarget),
            NextWeapon = keys && Input.IsActionPressed(FlightInput.NextWeapon),
            Chaff = keys && Input.IsActionPressed(FlightInput.Chaff),
            Flare = keys && Input.IsActionPressed(FlightInput.Flare),
            PipSystems = keys && Input.IsActionPressed(FlightInput.PipSystems),
            PipEngines = keys && Input.IsActionPressed(FlightInput.PipEngines),
            PipWeapons = keys && Input.IsActionPressed(FlightInput.PipWeapons),
            PipReset = keys && Input.IsActionPressed(FlightInput.PipReset),
            HeatSink = keys && Input.IsActionPressed(FlightInput.HeatSink),
            Jammer = keys && Input.IsActionPressed(FlightInput.Jammer),
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
        if (!Input.IsActionPressed(FlightInput.Zoom))
            _zoomHeld = false;
        bool zoom = !Typing && !_zoomHeld && !GameMenu.IsOpen && Input.IsActionPressed(FlightInput.Zoom);
        float fov = Typing ? _fov : zoom ? Mathf.Min(ZoomFov, _wheelFov) : _wheelFov;
        _camera.Fov = Mathf.Lerp(_camera.Fov, fov, 1 -Mathf.Exp(-ZoomSpeed * (float)delta));
        Lean(delta);
        if (_widget != null)
        {
            _widget.Visible = Mode == MouseUse.Flying;
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
            _hull ??= _ship.GetChildren().OfType<ShipHull>().FirstOrDefault();
            if (_hull?.Wrecked == true)
                state = _hull.Insured ? $"SHIP DESTROYED. The insurer's new ship in {Math.Ceiling(_hull.RespawnIn):0} s." : "SHIP DESTROYED.";
            else if (_hull != null)
                state += $"   Hull {_hull.Fraction * 100:0}%";
        }
        ShowModeBar(delta);
        string help = "";
        if (_noticeTime > 0)
        {
            _noticeTime -= delta;
            help = "\n" + _notice;
        }
        _hud.Text = $"{state}{help}";
    }

    // ---------- screens, buttons and saving ----------

    /// <summary>The bar at the bottom: what the mouse does now and the keys to change it; bright for a moment after a change.</summary>
    private void ShowModeBar(double delta)
    {
        if (_dot != null)
            _dot.Visible = Mode == MouseUse.Looking && !GameMenu.IsOpen;
        if (_modeBar == null)
            return;
        bool lost = Mode != MouseUse.Pointer && Input.MouseMode != Input.MouseModeEnum.Captured;
        string look = KeyOf(FlightInput.Look), pointer = KeyOf(FlightInput.Pointer);
        _modeBar.Text = GameMenu.IsOpen ? ""
            : Typing ? "TYPING ON THE COMPUTER    right-click: stop"
            : lost ? "Click to take the mouse"
            : Mode switch
            {
                MouseUse.Looking => $"LOOKING    click: work what the dot is on    {look}: fly (twice: look ahead)    {pointer}: pointer",
                MouseUse.Pointer => $"POINTER    click screens and controls    {pointer}: back    {look}: look around",
                _ => $"FLYING    {look}: look around    {pointer}: pointer    Esc: menu",
            };
        double age = Now - _modeShown;
        float alpha = Typing || lost ? 1 : age < 2 ? 1 : Mathf.Max(0.35f, 1 - (float)(age - 2));
        _modeBar.Modulate = new Color(1, 1, 1, alpha);
    }

    /// <summary>
    /// The head leans in to the screen being typed on, and back: the camera
    /// goes between where the head is and a view framing the screen.
    /// </summary>
    private void Lean(double delta)
    {
        float lean = Mathf.MoveToward(_lean, Typing ? 1 : 0, (float)delta / FocusSeconds);
        if (lean == 0 && _lean == 0)
            return;                             // the head is the pilot's (Look moves it)
        if (_lean == 0)
            _headPose = _camera.Transform;      // where it goes back to (the head doesn't turn while typing)
        _lean = lean;
        if (Typing)
            _framed = Framing(_focused);
        _camera.Transform = _headPose.InterpolateWith(_framed, Mathf.SmoothStep(0, 1, lean));
    }

    // In front of the screen (a quad facing its +Z), far enough for it to fill
    // the normal field of view, in the camera's parent's frame
    private Transform3D Framing(CockpitScreen screen)
    {
        Transform3D at = screen.GlobalTransform;
        Vector3 scale = at.Basis.Scale;
        Vector2 size = screen.Size * new Vector2(scale.X, scale.Y) * FocusMargin;
        Vector2 view = GetViewport().GetVisibleRect().Size;
        float tanUp = Mathf.Tan(Mathf.DegToRad(_fov) / 2), aspect = view.Y > 0 ? view.X / view.Y : 1;
        float distance = Mathf.Max(size.Y / 2 / tanUp, size.X / 2 / (tanUp * aspect));
        Basis basis = at.Basis.Orthonormalized();
        var global = new Transform3D(basis, at.Origin + basis.Z * distance);
        return _camera.GetParentNode3D().GlobalTransform.AffineInverse() * global;
    }

    private static string KeyOf(string action) => GameSettings.Describe(GameSettings.Binding(action));

    // The screen or control the pilot points at, within reach: the middle of
    // the view while looking around, the cursor while it is free.
    private Node PointedAt()
    {
        if (GameMenu.IsOpen)
            return null;
        if (Mode == MouseUse.Pointer)
        {
            Vector2 cursor = GetViewport().GetMousePosition();
            return Ray(_camera.ProjectRayOrigin(cursor), _camera.ProjectRayNormal(cursor));
        }
        return Mode == MouseUse.Looking && Input.MouseMode == Input.MouseModeEnum.Captured
            ? Ray(_camera.GlobalPosition, -_camera.GlobalBasis.Z) : null;
    }

    private Node Ray(Vector3 from, Vector3 direction)
    {
        Vector3 to = from + direction * ReachMetres;
        var query = PhysicsRayQueryParameters3D.Create(from, to);
        query.CollideWithAreas = true;
        query.CollideWithBodies = false;
        query.CollisionMask = ~Hitboxes.RoundLayer;      // the ship's hitboxes around the cockpit are for rounds, not hands
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
