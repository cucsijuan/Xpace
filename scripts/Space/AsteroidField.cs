using Godot;

namespace Xpace.Space;

/// <summary>
/// Rocks scattered in a box around the node, to fly between and to see the
/// ship move. Low-poly spheres, stretched and turned, in one MultiMesh.
/// No collisions yet.
/// </summary>
public partial class AsteroidField : MultiMeshInstance3D
{
    [Export] public int Count = 450;
    [Export] public Vector3 Extent = new(4000, 1200, 4000);
    [Export] public float MinRadius = 4f;
    [Export] public float MaxRadius = 90f;
    [Export] public int Seed = 4770;

    public override void _Ready()
    {
        var rng = new RandomNumberGenerator { Seed = (ulong)Seed };
        var rock = new SphereMesh { Radius = 1f, Height = 2f, RadialSegments = 7, Rings = 5 };
        rock.Material = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.42f, 0.38f, 0.34f),
            Roughness = 1f,
            VertexColorUseAsAlbedo = true,
        };
        var mm = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseColors = true,
            Mesh = rock,
            InstanceCount = Count,
        };
        for (int i = 0; i < Count; i++)
        {
            var position = new Vector3(
                rng.RandfRange(-Extent.X, Extent.X) / 2,
                rng.RandfRange(-Extent.Y, Extent.Y) / 2,
                rng.RandfRange(-Extent.Z, Extent.Z) / 2);
            // Many small rocks, a few big ones
            float radius = Mathf.Lerp(MinRadius, MaxRadius, Mathf.Pow(rng.Randf(), 3));
            var stretch = new Vector3(rng.RandfRange(0.7f, 1.3f), rng.RandfRange(0.6f, 1.1f), rng.RandfRange(0.8f, 1.5f)) * radius;
            var turn = new Basis(new Vector3(rng.Randf(), rng.Randf(), rng.Randf()).Normalized() + Vector3.Up * 0.01f, rng.RandfRange(0, Mathf.Tau));
            mm.SetInstanceTransform(i, new Transform3D(turn * Basis.FromScale(stretch), position));
            float shade = rng.RandfRange(0.7f, 1.15f);
            mm.SetInstanceColor(i, new Color(shade, shade * rng.RandfRange(0.92f, 1f), shade * rng.RandfRange(0.85f, 1f)));
        }
        Multimesh = mm;
    }
}
