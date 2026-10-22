using Godot;

namespace Xpace.Space;

/// <summary>A ring station of simple shapes, a landmark until there are real ones.</summary>
public partial class PlaceholderStation : Node3D
{
    [Export] public float RingRadius = 140f;
    [Export] public float Spin = 0.05f;                     // rad/s

    private Node3D _ring;

    public override void _Ready()
    {
        var hull = new StandardMaterial3D { AlbedoColor = new Color(0.55f, 0.57f, 0.6f), Metallic = 0.4f, Roughness = 0.5f };
        var dark = new StandardMaterial3D { AlbedoColor = new Color(0.2f, 0.21f, 0.23f), Roughness = 0.7f };
        var lights = new StandardMaterial3D
        {
            AlbedoColor = new Color(1f, 0.85f, 0.5f),
            EmissionEnabled = true,
            Emission = new Color(1f, 0.8f, 0.45f),
            EmissionEnergyMultiplier = 3f,
        };

        // The hub along Z, with docking lights at its mouth
        Add(this, new CylinderMesh { TopRadius = 28, BottomRadius = 28, Height = 260, RadialSegments = 24 }, hull,
            new Vector3(0, 0, 0), new Vector3(90, 0, 0));
        Add(this, new BoxMesh { Size = new Vector3(40, 16, 6) }, dark, new Vector3(0, 0, 131), Vector3.Zero);
        for (int i = 0; i < 8; i++)
        {
            float a = i * Mathf.Tau / 8;
            Add(this, new SphereMesh { Radius = 1.6f, Height = 3.2f }, lights,
                new Vector3(Mathf.Cos(a) * 32, Mathf.Sin(a) * 32, 131), Vector3.Zero);
        }

        // The ring, on spokes, turning
        _ring = new Node3D();
        AddChild(_ring);
        Add(_ring, new TorusMesh { InnerRadius = RingRadius - 18, OuterRadius = RingRadius + 18, Rings = 48, RingSegments = 12 }, hull,
            Vector3.Zero, new Vector3(90, 0, 0));
        for (int i = 0; i < 4; i++)
            Add(_ring, new BoxMesh { Size = new Vector3(RingRadius * 2, 8, 8) }, dark, Vector3.Zero, new Vector3(0, 0, i * 45));
    }

    public override void _Process(double delta)
    {
        _ring.RotateZ(Spin * (float)delta);
    }

    private static void Add(Node3D parent, Mesh mesh, Material material, Vector3 position, Vector3 rotationDegrees)
    {
        parent.AddChild(new MeshInstance3D
        {
            Mesh = mesh,
            MaterialOverride = material,
            Position = position,
            RotationDegrees = rotationDegrees,
        });
    }
}
