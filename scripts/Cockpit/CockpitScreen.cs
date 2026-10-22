using Godot;
using Xpace.Computer;
using Xpace.Ship;

namespace Xpace.Cockpit;

/// <summary>
/// A screen in the cockpit showing a computer's monitor, or one of the
/// ship's text displays. A monitor can be clicked (it has a collision area)
/// to give its computer the keyboard.
/// </summary>
public partial class CockpitScreen : MeshInstance3D
{
    [Export] public NodePath ComputerPath;

    /// <summary>Instead of a monitor: the ship's text displays, and which one (0 left, 1 right).</summary>
    [Export] public NodePath DisplayPath;
    [Export] public int DisplayIndex;

    /// <summary>Visible size in metres; a CRT is 4:3 whatever the video mode.</summary>
    [Export] public Vector2 Size = new(0.32f, 0.24f);

    public ShipComputer Computer { get; private set; }
    private DisplayDevice _display;

    private StandardMaterial3D _material;

    public override void _Ready()
    {
        Computer = ComputerPath == null || ComputerPath.IsEmpty ? null : GetNodeOrNull<ShipComputer>(ComputerPath);
        _display = DisplayPath == null || DisplayPath.IsEmpty ? null : GetNodeOrNull<DisplayDevice>(DisplayPath);
        Mesh = new QuadMesh { Size = Size };
        _material = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.Linear,
        };
        MaterialOverride = _material;

        if (Computer == null)
            return;                             // nothing to type on
        // An area, not a body: it rides along with the ship and only answers clicks
        var area = new Area3D { Monitoring = false };
        area.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(Size.X, Size.Y, 0.01f) } });
        AddChild(area);
    }

    public override void _Process(double delta)
    {
        // The computer makes its texture in its own _Ready, maybe after ours.
        if (_material.AlbedoTexture == null)
            _material.AlbedoTexture = _display != null ? _display.Screen(DisplayIndex) : Computer?.Screen;
    }
}
