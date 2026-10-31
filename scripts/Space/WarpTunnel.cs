using System.Linq;
using Godot;

namespace Xpace.Space;

/// <summary>
/// The rush out of a gate's ring, the way Synty's FX_WarpTunnel makes it: a
/// few pieces of tunnel at a time (sheets round the axis), born at the
/// centre and flying along it, each at a turn of its own about the axis and
/// slowly turning one way or the other, fading in and out over their short
/// life, added to what is behind (<c>synty_particle_add.gdshader</c>): seen
/// from the side, a long streak of light out of the ring; from the front,
/// the walls of a tunnel. They are not scaled with what they hang from
/// (Unity's local scaling): put the node where the scale is 1. A gate shows
/// it in its ring; a ship in transit is surrounded by one.
///
/// The wisps fly along the node's -Z. Without the tunnel model it shows
/// nothing.
/// </summary>
public partial class WarpTunnel : Node3D
{
    [Export] public PackedScene TunnelModel;                // its first mesh is a piece of the tunnel, along Y
    [Export] public Texture2D TunnelTexture;
    [Export] public float Size = 50f;                       // the mesh's largest scale (the smallest 0.9 of it)
    [Export] public float Speed = 150f;                     // m/s, along -Z
    [Export] public int Count = 5;                          // alive at once (a second's worth)
    [Export] public float Life = 1f;                        // s, the longest one lives (the shortest half that)
    [Export] public float SpinDegrees = 23f;                // a second, about the axis
    [Export] public Color Tint = new(0.5f, 0.5f, 0.5f, 0.5f);   // Synty's FX_WindTunnel_01 (0.5 is neutral)
    /// <summary>Each piece grows out of where it is born (its base there), not round it: a gate's rush leaves the ring.</summary>
    [Export] public bool FromBase = true;
    /// <summary>Where the pieces are born, metres along -Z (ahead of a ship in transit, with a negative speed: they come at it).</summary>
    [Export] public float StartAhead;

    public override void _Ready()
    {
        if (TunnelModel?.Instantiate() is not Node model)
            return;
        Mesh mesh = model.FindChildren("*", nameof(MeshInstance3D), true, false).OfType<MeshInstance3D>().FirstOrDefault()?.Mesh;
        model.Free();
        if (mesh == null)
            return;
        mesh = FromBase ? Based(mesh) : (Mesh)mesh.Duplicate();
        mesh.SurfaceSetMaterial(0, Look());
        float reach = Mathf.Abs(Speed) * Life + Size * 1.5f + Mathf.Abs(StartAhead);
        AddChild(new GpuParticles3D
        {
            Amount = Count,
            Lifetime = Life,
            Preprocess = Life * 5,
            LocalCoords = true,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            VisibilityAabb = new Aabb(new Vector3(-Size, -reach, -Size), new Vector3(Size * 2, reach * 2, Size * 2)),
            RotationDegrees = new Vector3(-90, 0, 0),       // the mesh's Y along the node's -Z
            ProcessMaterial = Process(),
            DrawPass1 = mesh,
        });
    }

    // The mesh moved along its length so its base is at the pivot: grown along +Y (the way the pieces fly)
    private static Mesh Based(Mesh mesh)
    {
        var made = new ArrayMesh();
        for (int i = 0; i < mesh.GetSurfaceCount(); i++)
        {
            Godot.Collections.Array arrays = mesh.SurfaceGetArrays(i);
            Vector3[] vertices = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
            float bottom = vertices.Min(v => v.Y);
            for (int v = 0; v < vertices.Length; v++)
                vertices[v].Y -= bottom;
            arrays[(int)Mesh.ArrayType.Vertex] = vertices;
            made.AddSurfaceFromArrays(mesh is ArrayMesh a ? a.SurfaceGetPrimitiveType(i) : Mesh.PrimitiveType.Triangles, arrays);
        }
        return made;
    }

    private ParticleProcessMaterial Process()
    {
        var fade = new Gradient
        {
            Offsets = new[] { 0f, 0.484f, 0.998f },
            Colors = new[] { Colors.White with { A = 0 }, Colors.White, Colors.White with { A = 0 } },
        };
        return new ParticleProcessMaterial
        {
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Point,
            EmissionShapeOffset = new Vector3(0, StartAhead, 0),
            Direction = new Vector3(0, 1, 0),
            Spread = 0,
            Gravity = Vector3.Zero,
            InitialVelocityMin = Speed,
            InitialVelocityMax = Speed,
            LifetimeRandomness = 0.5f,
            ParticleFlagRotateY = true,                 // turns about the axis
            AngleMin = 0,
            AngleMax = 360,
            AngularVelocityMin = -SpinDegrees,          // either way
            AngularVelocityMax = SpinDegrees,
            ScaleMin = Size * 0.9f,
            ScaleMax = Size,
            ColorRamp = new GradientTexture1D { Gradient = fade },
        };
    }

    private ShaderMaterial Look()
    {
        var look = new ShaderMaterial { Shader = GD.Load<Shader>("res://assets/materials/synty_particle_add.gdshader") };
        look.SetShaderParameter("tint", Tint);
        look.SetShaderParameter("main_texture", TunnelTexture);
        return look;
    }
}
