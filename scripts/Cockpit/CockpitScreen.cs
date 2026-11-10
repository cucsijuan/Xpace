using Godot;
using Xpace.Computer;
using Xpace.Controls;
using Xpace.Ship;

namespace Xpace.Cockpit;

/// <summary>What a cockpit screen shows.</summary>
public enum ScreenSource
{
    /// <summary>The ship computer's monitor; clicking it gives the computer the keyboard.</summary>
    Computer,
    /// <summary>One of the display card's screens, by name.</summary>
    Display,
}

/// <summary>
/// A screen in the cockpit. Place it in a cockpit scene over a screen of the
/// model and pick what it shows; it finds the ship's computer and display
/// card itself (the ShipBody it ends up under). Shown in the editor as a lit
/// rectangle of its size.
///
/// The pilot picks it by clicking it: a thin frame round it shows it is the
/// screen of interest (the screen keys act on it); a fainter one shows the
/// pilot is pointing at it.
/// </summary>
[Tool]
public partial class CockpitScreen : MeshInstance3D, IInteractive
{
    [Export] public ScreenSource Source;

    /// <summary>With Source Display: the name of the display card's screen (RADAR, WEAPON...).</summary>
    [Export] public string DisplayName = "";

    /// <summary>Visible size in metres; the picture is stretched to fit.</summary>
    [Export]
    public Vector2 Size
    {
        get => _size;
        set { _size = value; if (Mesh is QuadMesh quad) quad.Size = value; }
    }
    private Vector2 _size = new(0.32f, 0.24f);

    /// <summary>The tube behind it; none is a colour monitor for the computer and a monochrome one for the display card.</summary>
    [Export] public CrtMonitor Monitor;

    [Export] public Color FrameColor = new(1f, 0.85f, 0.2f);
    /// <summary>The frame's width and its gap from the picture, metres.</summary>
    [Export] public float FrameWidth = 0.0012f;
    [Export] public float FrameGap = 0.002f;

    public ShipComputer Computer { get; private set; }

    /// <summary>The screen of interest: the frame shows.</summary>
    public bool Selected
    {
        get => _selected;
        set { _selected = value; ShowFrame(); }
    }

    private static readonly Shader Tube = GD.Load<Shader>("res://assets/materials/crt_screen.gdshader");

    private ShaderMaterial _material;
    private ShipBody _ship;
    private StandardMaterial3D _frameMaterial;
    private Node3D _frame;
    private bool _bound, _selected, _hovered;

    public override void _Ready()
    {
        Mesh = new QuadMesh { Size = _size };
        if (Engine.IsEditorHint())
        {
            MaterialOverride = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                AlbedoColor = new Color(0.1f, 0.5f, 0.35f),
            };
            return;
        }
        _material = new ShaderMaterial { Shader = Tube };
        for (Node n = GetParent(); n != null && _ship == null; n = n.GetParent())
            _ship = n as ShipBody;
        Refit();
        if (_ship != null)
        {
            _ship.SpecChanged += Refit;
            TreeExiting += () => _ship.SpecChanged -= Refit;
        }
        MaterialOverride = _material;
        // An area, not a body: it rides along with the ship and only answers clicks
        var area = new Area3D { Monitoring = false };
        area.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(_size.X, _size.Y, 0.01f) } });
        AddChild(area);
        BuildFrame();
    }

    // The tube: the hull's own screens if it comes with some (a courtesy ship's old monochrome set), else this one's
    private void Refit() =>
        (_ship?.Spec?.Screens ?? Monitor ?? new CrtMonitor { Mask = Source == ScreenSource.Computer ? 0.2f : 0f }).Apply(_material);

    // Four thin strips round the picture, just in front of it
    private void BuildFrame()
    {
        _frameMaterial = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            AlbedoColor = FrameColor,
        };
        _frame = new Node3D { Visible = false };
        AddChild(_frame);
        float w = _size.X / 2 + FrameGap + FrameWidth / 2, h = _size.Y / 2 + FrameGap + FrameWidth / 2;
        float across = _size.X + 2 * (FrameGap + FrameWidth), up = _size.Y + 2 * FrameGap;
        foreach ((Vector3 at, Vector2 size) in new[]
        {
            (new Vector3(0, h, 0.001f), new Vector2(across, FrameWidth)),
            (new Vector3(0, -h, 0.001f), new Vector2(across, FrameWidth)),
            (new Vector3(-w, 0, 0.001f), new Vector2(FrameWidth, up)),
            (new Vector3(w, 0, 0.001f), new Vector2(FrameWidth, up)),
        })
            _frame.AddChild(new MeshInstance3D { Mesh = new QuadMesh { Size = size }, Position = at, MaterialOverride = _frameMaterial });
    }

    private void ShowFrame()
    {
        if (_frame == null)
            return;
        _frame.Visible = _selected || _hovered;
        _frameMaterial.AlbedoColor = new Color(FrameColor, _selected ? 1f : 0.35f);
    }

    public void Hover(bool on)
    {
        _hovered = on;
        ShowFrame();
    }

    /// <summary>
    /// Where an eye sees the screen fill a view of <paramref name="view"/> pixels at <paramref name="fov"/> degrees up
    /// and down, with <paramref name="margin"/> round it (1: edge to edge): in front of it, along its +Z (global).
    /// </summary>
    public Transform3D Framing(Vector2 view, float fov, float margin)
    {
        Transform3D at = GlobalTransform;
        Vector3 scale = at.Basis.Scale;
        Vector2 size = Size * new Vector2(scale.X, scale.Y) * margin;
        float tanUp = Mathf.Tan(Mathf.DegToRad(fov) / 2), aspect = view.Y > 0 ? view.X / view.Y : 1;
        float distance = Mathf.Max(size.Y / 2 / tanUp, size.X / 2 / (tanUp * aspect));
        Basis basis = at.Basis.Orthonormalized();
        return new Transform3D(basis, at.Origin + basis.Z * distance);
    }

    // Picking it is the pilot's business (it decides what a click on a screen means)
    public void Press() { }
    public void Release() { }
    public void Drag(Vector2 pixels) { }
    public void Wheel(int notches) { }

    public override void _Process(double delta)
    {
        if (_bound || Engine.IsEditorHint())
            return;
        // The ship sets up its computer and devices in its own _Ready, after ours.
        IComputerHost host = FindHost();
        if (host?.Computer == null)
            return;
        Computer = host.Computer;
        Texture2D picture = Source == ScreenSource.Computer
            ? Computer.Screen
            : (host as ShipBody)?.Device<DisplayDevice>()?.Screen(DisplayName);
        if (picture == null)
            return;
        _material.SetShaderParameter("picture", picture);
        _bound = true;
        Computer.Watch();
    }

    // Freed with the ship's tree, not on leaving it: a ship moved to another sector leaves and enters again
    public override void _Notification(int what)
    {
        if (what == NotificationPredelete && _bound && Computer != null && IsInstanceValid(Computer))
            Computer.Unwatch();
    }

    // The ship (or terminal) whose computer it shows: the nearest above it
    private IComputerHost FindHost()
    {
        for (Node n = GetParent(); n != null; n = n.GetParent())
            if (n is IComputerHost host)
                return host;
        return null;
    }
}
