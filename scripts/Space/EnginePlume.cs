using System;
using System.Linq;
using Godot;

namespace Xpace.Space;

/// <summary>
/// An engine's flame, as hard as the engine pushes, the way Synty's
/// FX_Flame_Booster_Round makes it: a few flame meshes on top of each other,
/// each born at the nozzle with a turn of its own, spinning about its length,
/// swelling and shrinking over its short life, blue going to white as it
/// fades in and out, added on top of what is behind (glowing gas: it
/// lights, it does not hide). The texture's streaks run along the flame,
/// bright at the nozzle.
///
/// Put the node at the nozzle with its +Z pointing aft. Whoever knows the
/// push sets <see cref="Thrust"/> (a remote ship from its owner's
/// snapshots): nothing burns with the engine idle, more flames and a longer
/// one the harder it pushes. Without the flame model it falls back to a
/// cone.
/// </summary>
public partial class EnginePlume : Node3D
{
    [Export] public PackedScene FlameModel;                 // its first mesh is the flame (along -Y, the nozzle at 0)
    [Export] public Texture2D FlameTexture;
    [Export] public float FlameSize = 14f;                  // the mesh's scale at the flames' fullest
    [Export] public int Flames = 12;                        // at full push
    [Export] public float Life = 1f;                        // s, the longest a flame lives (the shortest half that)
    [Export] public float SpinDegrees = 900f;               // a second, about the flame's length
    [Export] public float LongestAtFull = 2.5f;             // how much longer the flames are at full push than at the least
    [Export] public float MinThrust = 0.03f;                // below this, nothing burns
    [Export] public Color Hot = new(0f, 0.5f, 1f);          // the young flame's colour (it ends white)
    [Export] public float LightEnergy = 1.2f;
    [Export] public float LightRange = 12f;

    /// <summary>0 to 1: the engine's push, of the most it gives.</summary>
    public float Thrust { get; set; }

    private GpuParticles3D _flames;
    private OmniLight3D _light;

    public override void _Ready()
    {
        _flames = new GpuParticles3D
        {
            Amount = Flames,
            Lifetime = Life,
            LocalCoords = true,
            Emitting = false,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            VisibilityAabb = new Aabb(new Vector3(-3, -6, -3), new Vector3(6, 7, 6)),
            RotationDegrees = new Vector3(-90, 0, 0),       // the flame's -Y aft, along the node's +Z
            ProcessMaterial = Process(),
            DrawPass1 = Flame(),
        };
        _flames.DrawPass1.SurfaceSetMaterial(0, Look());
        AddChild(_flames);
        _light = new OmniLight3D { LightColor = Hot.Lerp(Colors.White, 0.4f), OmniRange = LightRange, LightEnergy = 0, Position = new Vector3(0, 0, 3) };
        AddChild(_light);
    }

    public override void _Process(double delta)
    {
        float push = Math.Clamp(Thrust, 0, 1);
        bool burning = push >= MinThrust;
        if (_flames.Emitting != burning)
            _flames.Emitting = burning;
        _flames.AmountRatio = Math.Max(push, 0.2f);
        float length = 1 + (LongestAtFull - 1) * push;
        _flames.Scale = new Vector3(1, length, 1);
        _light.LightEnergy = burning ? LightEnergy * push : 0;
    }

    private Mesh Flame()
    {
        if (FlameModel?.Instantiate() is Node model)
        {
            Mesh mesh = model.FindChildren("*", nameof(MeshInstance3D), true, false).OfType<MeshInstance3D>().FirstOrDefault()?.Mesh;
            model.Free();
            if (mesh != null)
                return (Mesh)mesh.Duplicate();
        }
        var cone = new CylinderMesh { TopRadius = 0.08f, BottomRadius = 0, Height = 0.24f };
        return (Mesh)cone.Duplicate();
    }

    private ParticleProcessMaterial Process()
    {
        var swell = new Curve();                        // nothing at birth, fullest halfway, nothing at death
        swell.AddPoint(new Vector2(0, 0));
        swell.AddPoint(new Vector2(0.5f, 1));
        swell.AddPoint(new Vector2(1, 0));
        var colour = new Gradient
        {
            Offsets = new[] { 0f, 0.33f, 0.54f, 1f },
            Colors = new[]
            {
                Hot with { A = 0 },
                Hot with { A = 0.6f },
                Hot.Lerp(Colors.White, 0.3f) with { A = 1 },
                Colors.White with { A = 0 },
            },
        };
        return new ParticleProcessMaterial
        {
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Point,
            Gravity = Vector3.Zero,
            InitialVelocityMin = 0,
            InitialVelocityMax = 0,
            LifetimeRandomness = 0.5f,
            ParticleFlagRotateY = true,                 // turns about the flame's length
            AngleMin = 0,
            AngleMax = 360,
            AngularVelocityMin = SpinDegrees,
            AngularVelocityMax = SpinDegrees,
            ScaleMin = FlameSize * 0.5f,
            ScaleMax = FlameSize,
            ScaleCurve = new CurveTexture { Curve = swell },
            ColorRamp = new GradientTexture1D { Gradient = colour },
        };
    }

    private StandardMaterial3D Look() => new()
    {
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        BlendMode = BaseMaterial3D.BlendModeEnum.Add,
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        DepthDrawMode = BaseMaterial3D.DepthDrawModeEnum.Disabled,
        CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        VertexColorUseAsAlbedo = true,
        AlbedoTexture = FlameTexture,
        AlbedoColor = new Color(1.6f, 1.6f, 1.6f),          // bright as Synty's (emission 2.18), less our glow
    };
}
