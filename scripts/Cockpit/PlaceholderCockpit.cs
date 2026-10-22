using Godot;

namespace Xpace.Cockpit;

/// <summary>
/// Boxes in the shape of a fighter cockpit around the seat (the origin, the
/// pilot looking down -Z), until the Synty cockpits come in.
/// </summary>
public partial class PlaceholderCockpit : Node3D
{
    [Export] public Color Hull = new(0.16f, 0.17f, 0.19f);
    [Export] public Color Trim = new(0.08f, 0.08f, 0.09f);

    /// <summary>The ship's text displays, for the two side screens.</summary>
    [Export] public NodePath DisplayPath;

    public override void _Ready()
    {
        var hull = new StandardMaterial3D { AlbedoColor = Hull, Roughness = 0.8f };
        var trim = new StandardMaterial3D { AlbedoColor = Trim, Roughness = 0.6f };

        // Dashboard and the bezel of the main screen
        Box(hull, new Vector3(1.3f, 0.35f, 0.5f), new Vector3(0, 0.6f, -0.78f));
        Box(trim, new Vector3(0.44f, 0.35f, 0.05f), new Vector3(0, 0.84f, -0.615f), new Vector3(-20, 0, 0));

        // Side screens: the ship's two text displays
        SideScreen(trim, new Vector3(-0.5f, 0.86f, -0.62f), new Vector3(-15, 30, 0), 0);
        SideScreen(trim, new Vector3(0.5f, 0.86f, -0.62f), new Vector3(-15, -30, 0), 1);

        // Side consoles
        Box(hull, new Vector3(0.25f, 0.2f, 0.9f), new Vector3(-0.55f, 0.55f, -0.25f));
        Box(hull, new Vector3(0.25f, 0.2f, 0.9f), new Vector3(0.55f, 0.55f, -0.25f));

        // Canopy frame: pillars, the arch over the head, the sills
        Box(trim, new Vector3(0.05f, 0.85f, 0.05f), new Vector3(-0.62f, 1.25f, -0.85f), new Vector3(-30, 0, 12));
        Box(trim, new Vector3(0.05f, 0.85f, 0.05f), new Vector3(0.62f, 1.25f, -0.85f), new Vector3(-30, 0, -12));
        Box(trim, new Vector3(1.0f, 0.05f, 0.05f), new Vector3(0, 1.62f, -0.55f));
        Box(trim, new Vector3(0.05f, 0.05f, 1.6f), new Vector3(0, 1.66f, 0.2f));
        Box(trim, new Vector3(0.05f, 0.05f, 1.8f), new Vector3(-0.7f, 0.9f, -0.1f));
        Box(trim, new Vector3(0.05f, 0.05f, 1.8f), new Vector3(0.7f, 0.9f, -0.1f));

        // Seat back
        Box(hull, new Vector3(0.6f, 0.9f, 0.12f), new Vector3(0, 0.95f, 0.35f));
    }

    private void SideScreen(Material bezel, Vector3 position, Vector3 rotationDegrees, int index)
    {
        Box(bezel, new Vector3(0.24f, 0.2f, 0.05f), position, rotationDegrees);
        Node display = DisplayPath == null || DisplayPath.IsEmpty ? null : GetNodeOrNull(DisplayPath);
        if (display == null)
            return;
        Vector3 facing = Basis.FromEuler(rotationDegrees * (Mathf.Pi / 180f)).Z;
        AddChild(new CockpitScreen
        {
            DisplayPath = display.GetPath(),
            DisplayIndex = index,
            Size = new Vector2(0.2f, 0.16f),
            Position = position + facing * 0.027f,
            RotationDegrees = rotationDegrees,
        });
    }

    private void Box(Material material, Vector3 size, Vector3 position, Vector3 rotationDegrees = default)
    {
        AddChild(new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = size },
            MaterialOverride = material,
            Position = position,
            RotationDegrees = rotationDegrees,
        });
    }
}
