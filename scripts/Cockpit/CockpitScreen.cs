using Godot;
using Xpace.Computer;
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
/// </summary>
[Tool]
public partial class CockpitScreen : MeshInstance3D
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

    public ShipComputer Computer { get; private set; }

    private StandardMaterial3D _material;
    private bool _bound;

    public override void _Ready()
    {
        Mesh = new QuadMesh { Size = _size };
        _material = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.Linear,
            AlbedoColor = Engine.IsEditorHint() ? new Color(0.1f, 0.5f, 0.35f) : Colors.White,
        };
        MaterialOverride = _material;
        if (Engine.IsEditorHint() || Source != ScreenSource.Computer)
            return;
        // An area, not a body: it rides along with the ship and only answers clicks
        var area = new Area3D { Monitoring = false };
        area.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(_size.X, _size.Y, 0.01f) } });
        AddChild(area);
    }

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
        _material.AlbedoTexture = picture;
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
