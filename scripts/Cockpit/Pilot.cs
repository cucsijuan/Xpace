using Godot;
using Xpace.Computer;

namespace Xpace.Cockpit;

/// <summary>
/// The seated pilot: looks around with the mouse, clicks a cockpit screen to
/// type on its computer, right-clicks to let go of it.
///
/// The first click takes the mouse; Esc (while not typing) gives it back.
/// F5 saves the computer, F9 loads it.
/// </summary>
public partial class Pilot : Node3D
{
    [Export] public float MouseSensitivity = 0.0025f;
    [Export] public float MaxYaw = Mathf.Pi * 0.6f;
    [Export] public float MaxPitch = Mathf.Pi * 0.4f;
    [Export] public float ReachMetres = 1.5f;
    [Export] public string SavePath = "user://computer.sav";

    private Camera3D _camera;
    private Label _hud;
    private CockpitScreen _focused;
    private float _yaw, _pitch;
    private string _notice = "";
    private double _noticeTime;
    private int _settleFrames;

    public override void _Ready()
    {
        _camera = GetNode<Camera3D>("Camera3D");
        _hud = GetNodeOrNull<Label>("Hud/Label");
    }

    // Capturing warps the cursor to the middle of the window, and that jump
    // comes back as motion: the first frames after it do not turn the head.
    private void CaptureMouse()
    {
        Input.MouseMode = Input.MouseModeEnum.Captured;
        _settleFrames = 3;
    }

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
        switch (e)
        {
            case InputEventMouseMotion motion when Input.MouseMode == Input.MouseModeEnum.Captured && _settleFrames == 0:
                _yaw = Mathf.Clamp(_yaw - motion.Relative.X * MouseSensitivity, -MaxYaw, MaxYaw);
                _pitch = Mathf.Clamp(_pitch - motion.Relative.Y * MouseSensitivity, -MaxPitch, MaxPitch);
                _camera.Rotation = new Vector3(_pitch, _yaw, 0);
                break;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }:
                if (Input.MouseMode != Input.MouseModeEnum.Captured)
                    CaptureMouse();
                else
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

    public override void _Process(double delta)
    {
        if (_settleFrames > 0)
            _settleFrames--;
        if (_hud == null)
            return;
        ShipComputer computer = FindComputer();
        string state = computer == null ? "" :
            computer.Error != "" ? $"Computer: {computer.Error}" :
            $"Computer: {(computer.Powered ? "on" : "off")}, {computer.EmulatedSeconds:0.0} s{(computer.DriveLight ? ", drive A:" : "")}";
        string help = _focused != null
            ? "Typing on the computer. Right-click to let go."
            : Input.MouseMode != Input.MouseModeEnum.Captured
                ? "Click to look around."
                : "Click a screen to type on it. F5 save, F9 load, Esc frees the mouse.";
        if (_noticeTime > 0)
        {
            _noticeTime -= delta;
            help += "\n" + _notice;
        }
        _hud.Text = $"{state}\n{help}";
    }

    private CockpitScreen ScreenAhead()
    {
        Vector3 from = _camera.GlobalPosition;
        Vector3 to = from - _camera.GlobalBasis.Z * ReachMetres;
        var hit = GetWorld3D().DirectSpaceState.IntersectRay(PhysicsRayQueryParameters3D.Create(from, to));
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
        _focused?.Computer ?? GetTree().Root.FindChild("ShipComputer", true, false) as ShipComputer;

    private void Notice(string text)
    {
        _notice = text;
        _noticeTime = 3;
    }
}
