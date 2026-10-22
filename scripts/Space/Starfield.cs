using Godot;

namespace Xpace.Space;

/// <summary>Stars on a far sphere around the camera: a backdrop until there is a sky.</summary>
public partial class Starfield : MultiMeshInstance3D
{
    [Export] public int Count = 2000;
    [Export] public float Distance = 900f;
    [Export] public int Seed = 86;

    public override void _Ready()
    {
        var rng = new RandomNumberGenerator { Seed = (ulong)Seed };
        var star = new SphereMesh { Radius = 1f, Height = 2f, RadialSegments = 6, Rings = 3 };
        star.Material = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            VertexColorUseAsAlbedo = true,
        };
        var mm = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseColors = true,
            Mesh = star,
            InstanceCount = Count,
        };
        for (int i = 0; i < Count; i++)
        {
            // A uniform direction, a size, and a tint from red to blue-white
            float z = rng.RandfRange(-1, 1), a = rng.RandfRange(0, Mathf.Tau), r = Mathf.Sqrt(1 - z * z);
            var dir = new Vector3(r * Mathf.Cos(a), z, r * Mathf.Sin(a));
            float size = Mathf.Pow(rng.Randf(), 4) * 2.5f + 0.6f;
            mm.SetInstanceTransform(i, new Transform3D(Basis.Identity.Scaled(Vector3.One * size), dir * Distance));
            float t = rng.Randf(), b = rng.RandfRange(0.5f, 1f);
            mm.SetInstanceColor(i, new Color(1f, 0.8f + 0.2f * t, 0.6f + 0.4f * t) * b);
        }
        Multimesh = mm;
        CastShadow = ShadowCastingSetting.Off;
    }

    public override void _Process(double delta)
    {
        // A sky: always the same distance away, wherever the camera goes
        Camera3D camera = GetViewport().GetCamera3D();
        if (camera != null)
            GlobalPosition = camera.GlobalPosition;
    }
}
