using Godot;

namespace Xpace.Space;

/// <summary>
/// Specks left behind in world space around the ship as it flies: without
/// them there is nothing near enough to show speed or drift.
/// </summary>
public partial class SpaceDust : GpuParticles3D
{
    [Export] public float Spread = 160f;
    [Export] public int Specks = 900;
    [Export] public float Life = 6f;

    public override void _Ready()
    {
        Amount = Specks;
        Lifetime = Life;
        Preprocess = Life;
        LocalCoords = false;                    // they stay where they were made
        VisibilityAabb = new Aabb(-Vector3.One * Spread * 2, Vector3.One * Spread * 4);
        ProcessMaterial = new ParticleProcessMaterial
        {
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box,
            EmissionBoxExtents = Vector3.One * Spread,
            Gravity = Vector3.Zero,
            InitialVelocityMin = 0,
            InitialVelocityMax = 0,
            ScaleMin = 0.6f,
            ScaleMax = 1.4f,
            Color = new Color(0.8f, 0.85f, 0.9f, 0.55f),
        };
        DrawPass1 = new QuadMesh
        {
            Size = new Vector2(0.12f, 0.12f),
            Material = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles,
                VertexColorUseAsAlbedo = true,
            },
        };
    }
}
