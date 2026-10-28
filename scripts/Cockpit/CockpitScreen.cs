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
        (Monitor ?? new CrtMonitor { Mask = Source == ScreenSource.Computer ? 0.2f : 0f }).Apply(_material);
        MaterialOverride = _material;
        // An area, not a body: it rides along with the ship and only answers clicks
        var area = new Area3D { Monitoring = false };
        area.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(_size.X, _size.Y, 0.01f) } });
        AddChild(area);
        BuildFrame();
    }

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
        ShipBody ship = FindShip();
        if (ship?.Computer == null)
            return;
        Computer = ship.Computer;
        Texture2D picture = Source == ScreenSource.Computer
            ? Computer.Screen
            : ship.Device<DisplayDevice>()?.Screen(DisplayName);
        if (picture == null)
            return;
        _material.SetShaderParameter("picture", picture);
        _bound = true;
    }

    private ShipBody FindShip()
    {
        for (Node n = GetParent(); n != null; n = n.GetParent())
            if (n is ShipBody ship)
                return ship;
        return null;
    }
}
