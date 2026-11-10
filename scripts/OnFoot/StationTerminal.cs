using Godot;
using Xpace.Computer;
using Xpace.Space;

namespace Xpace.OnFoot;

/// <summary>
/// A computer of the station's own a walker can use (scenes/stations/props/registry_terminal.tscn): a pc86 machine
/// (<see cref="ShipComputer"/>, its diskette the terminal's software) with its screen (<see cref="Cockpit.CockpitScreen"/>)
/// and its cards: a <see cref="RegistryDevice"/> links it to the station's registry. It runs while a walker is within
/// <see cref="PowerRange"/> (it boots when one comes near) and stops when nobody is.
///
/// Using it is typing on it, as a pilot types on the ship's computer: the view moves in front of its screen, the keys
/// go to its keyboard, and a right click (or walking off) leaves it. It serves the station it is part of
/// (<see cref="StationRadio.Above"/>).
/// </summary>
public partial class StationTerminal : Node3D, IWalkerUse, IComputerHost
{
    /// <summary>What using it does, for the walker's prompt.</summary>
    [Export] public string Prompt = "use the terminal";
    /// <summary>Where a walker stands to use it; none: the terminal itself.</summary>
    [Export] public Node3D Front;
    [Export] public float UseDistance = 1.4f;
    /// <summary>Its screen: the view frames it while typing.</summary>
    [Export] public Cockpit.CockpitScreen Screen;
    /// <summary>A walker this near keeps it running.</summary>
    [Export] public float PowerRange = 15;
    /// <summary>Runs whoever is near or not (a showpiece, a test).</summary>
    [Export] public bool AlwaysOn;
    /// <summary>How much room round the screen the framed view leaves, and how long the view takes to move in.</summary>
    [Export] public float FocusMargin = 1.15f;
    [Export] public float FocusSeconds = 0.5f;

    public ShipComputer Computer { get; private set; }
    public RegistryDevice Registry { get; private set; }

    /// <summary>The walker typing on it, or null.</summary>
    public Walker User { get; private set; }

    private Camera3D _view;
    private Transform3D _from;
    private float _lean;
    private bool _leaving;

    public override void _Ready()
    {
        AddToGroup(Walker.UsesGroup);
        foreach (Node n in GetChildren())
        {
            Computer ??= n as ShipComputer;
            Registry ??= n as RegistryDevice;
        }
        if (Computer != null)
            Computer.RunsItself = false;
        if (Registry != null)
            Registry.Station = StationRadio.Above(this)?.Id;
        _view = new Camera3D { Current = false };
        AddChild(_view);
    }

    public string Offer(Walker walker) =>
        User == null && Computer != null && walker.GlobalPosition.DistanceTo((Front ?? this).GlobalPosition) < UseDistance ? Prompt : null;

    public void Use(Walker walker)
    {
        if (User != null || Computer == null)
            return;
        User = walker;
        walker.Busy = true;
        Input.MouseMode = Input.MouseModeEnum.Visible;
        Camera3D eye = walker.Camera ?? GetViewport().GetCamera3D();
        _from = eye?.GlobalTransform ?? GlobalTransform;
        _view.Fov = eye?.Fov ?? 60;
        _view.GlobalTransform = _from;
        _view.MakeCurrent();
        _lean = 0;
        _leaving = false;
        if (!Computer.Powered)
            Computer.PowerOn();
    }

    /// <summary>Stops typing: the view goes back to the walker.</summary>
    public void Leave()
    {
        if (User != null)
            _leaving = true;
    }

    public override void _Input(InputEvent e)
    {
        if (User == null || _leaving)
            return;
        if (e is InputEventKey key)
        {
            if (!key.Echo)                      // the PC's keyboard repeats keys itself
                Computer.Key(KeyMap.CodeOf(key), key.Pressed);
            GetViewport().SetInputAsHandled();
        }
        else if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right })
        {
            GetViewport().SetInputAsHandled();
            Leave();
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (Computer == null)
            return;
        bool near = AlwaysOn || User != null || (Walker.Local is Walker w && IsInstanceValid(w) && w.GlobalPosition.DistanceTo(GlobalPosition) < PowerRange);
        if (near && !Computer.Powered)
            Computer.PowerOn();
        else if (!near && Computer.Powered)
            Computer.PowerOff();
        if (!Computer.Powered)
            return;
        Registry?.BeforeStep(Computer, delta);
        Computer.Advance(delta);
        Registry?.AfterStep(Computer, delta);
    }

    public override void _Process(double delta)
    {
        if (User == null)
            return;
        if (!IsInstanceValid(User))
        {
            Done();
            return;
        }
        _lean = Mathf.MoveToward(_lean, _leaving ? 0 : 1, (float)delta / FocusSeconds);
        Transform3D framed = Screen?.Framing(GetViewport().GetVisibleRect().Size, _view.Fov, FocusMargin) ?? _from;
        _view.GlobalTransform = _from.InterpolateWith(framed, Mathf.SmoothStep(0, 1, _lean));
        if (_leaving && _lean <= 0)
            Done();
    }

    private void Done()
    {
        Computer?.ReleaseKeys();
        Walker walker = User;
        User = null;
        _leaving = false;
        if (walker != null && IsInstanceValid(walker))
        {
            walker.Busy = false;
            walker.TakeControl();
        }
    }
}
