using Godot;
using Xpace.Computer;

namespace Xpace.Cockpit;

/// <summary>
/// A screen in the cockpit showing a computer's monitor. It can be clicked
/// (it has a collision area) to give it the keyboard.
/// </summary>
public partial class CockpitScreen : MeshInstance3D
{
    [Export] public NodePath ComputerPath;

    /// <summary>Visible size in metres; a CRT is 4:3 whatever the video mode.</summary>
    [Export] public Vector2 Size = new(0.32f, 0.24f);

    public ShipComputer Computer { get; private set; }

    private StandardMaterial3D _material;

    public override void _Ready()
    {
        Computer = GetNodeOrNull<ShipComputer>(ComputerPath);
        Mesh = new QuadMesh { Size = Size };
        _material = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.Linear,
        };
        MaterialOverride = _material;

        // An area, not a body: it rides along with the ship and only answers clicks
        var area = new Area3D { Monitoring = false };
        area.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(Size.X, Size.Y, 0.01f) } });
        AddChild(area);
    }

    public override void _Process(double delta)
    {
        // The computer makes its texture in its own _Ready, maybe after ours.
        if (_material.AlbedoTexture == null && Computer?.Screen != null)
            _material.AlbedoTexture = Computer.Screen;
    }
}
