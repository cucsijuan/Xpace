using System;
using System.Linq;
using Godot;

namespace Xpace.Space;

/// <summary>
/// An explosion made of Synty's layers (FX_Missiles' and FX_Explosion's):
/// a burst of fireballs that swell and fade, sparks thrown out and
/// stretched along their flight, and, for the big ones, debris that burns
/// as it flies (each piece leaving fire behind) and a cloud of smoke that
/// swells over where the ship was (FX_MissileCloud_01's look: the cloud
/// texture blended, not added). Every layer is set in the
/// scene (scenes/fx); a layer with no count is left out. Lit by a flash
/// that dies with the fire.
///
/// <see cref="Explosion.Spawn"/> places one and sets <see cref="Size"/>:
/// the scene's <see cref="ReferenceSize"/> is the explosion drawn as set,
/// a bigger or smaller one is the same scaled. It frees itself when done.
/// </summary>
public partial class ExplosionFx : Node3D
{
    [Export] public float ReferenceSize = 14f;

    [ExportGroup("Fireball")]
    [Export] public Texture2D FireTexture;                  // ExplosionCloud, added on top
    [Export] public Vector2I FireCount = new(15, 30);
    [Export] public Vector2 FireSize = new(1, 4);           // m
    [Export] public Vector2 FireLife = new(0.1f, 1f);       // s
    [Export] public Gradient FireColors;                    // over its life (alpha too)
    [Export] public Curve FireGrowth;                       // size over its life (0 to 1)
    [Export] public float FireRadius = 1f;                  // m, the sphere it starts in

    [ExportGroup("Smoke")]
    [Export] public Texture2D SmokeTexture;                 // ExplosionCloud, blended
    [Export] public Vector2I SmokeCount;
    [Export] public Vector2 SmokeSize = new(6, 12);         // m, at its fullest
    [Export] public Vector2 SmokeSpeed = new(0.5f, 4);      // m/s, outward
    [Export] public Vector2 SmokeLife = new(3, 6);
    [Export] public Gradient SmokeColors;                   // over its life (alpha too)
    [Export] public Curve SmokeGrowth;
    [Export] public float SmokeRadius = 2f;

    [ExportGroup("Sparks")]
    [Export] public Texture2D SparkTexture;                 // SphereGlow, added on top
    [Export] public Vector2I SparkCount = new(20, 60);
    [Export] public Vector2 SparkSize = new(0.01f, 0.05f);  // m across
    [Export] public float SparkStretch = 12f;               // long as many times as wide
    [Export] public Vector2 SparkSpeed = new(1, 30);        // m/s
    [Export] public Vector2 SparkLife = new(0.3f, 1f);
    [Export] public Color SparkColor = new(1, 0.85f, 0.6f);

    [ExportGroup("Debris")]
    [Export] public PackedScene DebrisModel;                // SM_Pebble_Debris_03
    [Export] public Material DebrisMaterial;
    [Export] public int DebrisCount;
    [Export] public Vector2 DebrisSize = new(0.1f, 1f);     // times the model
    [Export] public Vector2 DebrisSpeed = new(1, 20);
    [Export] public Vector2 DebrisLife = new(0.5f, 3f);
    [Export] public float DebrisSpinDegrees = 90f;          // a second, at most
    [Export] public float DebrisFireRate = 50f;             // fire puffs each piece leaves a second (FX_Explosion_Trail)
    [Export] public Vector2 DebrisFireSize = new(0.2f, 0.5f);
    [Export] public Vector2 DebrisFireLife = new(0.5f, 1f);
    [Export] public Gradient DebrisFireColors;

    [ExportGroup("Light")]
    [Export] public Color LightColor = new(1f, 0.6f, 0.3f);

    /// <summary>How big it is (the scene as set is <see cref="ReferenceSize"/>).</summary>
    public float Size { get; set; } = 14f;
    /// <summary>The flash's brightness at the start, and how long it takes to die.</summary>
    public float LightEnergy { get; set; } = 10f;
    public double LightSeconds { get; set; } = 0.8;

    private float _k = 1f;
    private double _longest;

    public override void _Ready()
    {
        _k = Size / Math.Max(ReferenceSize, 0.01f);
        if (SmokeCount.Y > 0)
            Add(Smoke());
        if (FireCount.Y > 0)
            Add(Fireball());
        if (SparkCount.Y > 0)
            Add(Sparks());
        if (DebrisCount > 0 && Debris() is GpuParticles3D debris)
            Add(debris);

        var light = new OmniLight3D { LightColor = LightColor, LightEnergy = LightEnergy, OmniRange = Size * 5 };
        AddChild(light);
        var t = CreateTween();
        t.TweenProperty(light, "light_energy", 0f, LightSeconds);
        GetTree().CreateTimer(Math.Max(_longest, LightSeconds) + 0.2, false, true).Timeout += QueueFree;
    }

    private void Add(GpuParticles3D p)
    {
        _longest = Math.Max(_longest, p.Lifetime);
        AddChild(p);
        p.Emitting = true;
    }

    private static int Between(Vector2I range) => (int)GD.RandRange(Math.Max(range.X, 1), Math.Max(range.Y, Math.Max(range.X, 1)));

    private GpuParticles3D Burst(int amount, Vector2 life) => new()
    {
        Amount = amount,
        Lifetime = life.Y,
        OneShot = true,
        Explosiveness = 1,
        LocalCoords = true,                             // the node stays put, and moves with the floating origin
        Emitting = false,
        CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        VisibilityAabb = new Aabb(Vector3.One * -100 * _k, Vector3.One * 200 * _k),
    };

    private GpuParticles3D Fireball()
    {
        GpuParticles3D p = Burst(Between(FireCount), FireLife);
        p.ProcessMaterial = new ParticleProcessMaterial
        {
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Sphere,
            EmissionSphereRadius = FireRadius * _k,
            Gravity = Vector3.Zero,
            InitialVelocityMin = 0,
            InitialVelocityMax = 0,
            LifetimeRandomness = 1 - FireLife.X / FireLife.Y,
            AngleMin = 0,
            AngleMax = 360,
            ScaleMin = FireSize.X * _k,
            ScaleMax = FireSize.Y * _k,
            ScaleCurve = FireGrowth != null ? new CurveTexture { Curve = FireGrowth } : null,
            ColorRamp = FireColors != null ? new GradientTexture1D { Gradient = FireColors } : null,
        };
        p.DrawPass1 = new QuadMesh { Material = Glow(FireTexture, BaseMaterial3D.BillboardModeEnum.Particles) };
        return p;
    }

    private GpuParticles3D Smoke()
    {
        GpuParticles3D p = Burst(Between(SmokeCount), SmokeLife);
        p.ProcessMaterial = new ParticleProcessMaterial
        {
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Sphere,
            EmissionSphereRadius = SmokeRadius * _k,
            Direction = Vector3.Forward,
            Spread = 180,
            Gravity = Vector3.Zero,
            InitialVelocityMin = SmokeSpeed.X * _k,
            InitialVelocityMax = SmokeSpeed.Y * _k,
            LifetimeRandomness = 1 - SmokeLife.X / SmokeLife.Y,
            AngleMin = 0,
            AngleMax = 360,
            AngularVelocityMin = -20,
            AngularVelocityMax = 20,
            ScaleMin = SmokeSize.X * _k,
            ScaleMax = SmokeSize.Y * _k,
            ScaleCurve = SmokeGrowth != null ? new CurveTexture { Curve = SmokeGrowth } : null,
            ColorRamp = SmokeColors != null ? new GradientTexture1D { Gradient = SmokeColors } : null,
        };
        StandardMaterial3D look = Glow(SmokeTexture, BaseMaterial3D.BillboardModeEnum.Particles);
        look.BlendMode = BaseMaterial3D.BlendModeEnum.Mix;
        p.DrawPass1 = new QuadMesh { Material = look };
        return p;
    }

    private GpuParticles3D Sparks()
    {
        GpuParticles3D p = Burst(Between(SparkCount), SparkLife);
        var swell = new Curve();                    // nothing, fullest halfway, nothing
        swell.AddPoint(new Vector2(0, 0));
        swell.AddPoint(new Vector2(0.5f, 1));
        swell.AddPoint(new Vector2(1, 0));
        p.ProcessMaterial = new ParticleProcessMaterial
        {
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Point,
            Direction = Vector3.Forward,
            Spread = 180,
            Gravity = Vector3.Zero,
            InitialVelocityMin = SparkSpeed.X * _k,
            InitialVelocityMax = SparkSpeed.Y * _k,
            LifetimeRandomness = 1 - SparkLife.X / SparkLife.Y,
            ParticleFlagAlignY = true,              // along their flight
            ScaleMin = SparkSize.X * _k,
            ScaleMax = SparkSize.Y * _k,
            ScaleCurve = new CurveTexture { Curve = swell },
            Color = SparkColor,
        };
        StandardMaterial3D look = Glow(SparkTexture, BaseMaterial3D.BillboardModeEnum.FixedY);
        p.DrawPass1 = new QuadMesh { Size = new Vector2(1, SparkStretch), Material = look };
        return p;
    }

    private GpuParticles3D Debris()
    {
        Mesh mesh = null;
        if (DebrisModel?.Instantiate() is Node model)
        {
            mesh = model.FindChildren("*", nameof(MeshInstance3D), true, false).OfType<MeshInstance3D>().FirstOrDefault()?.Mesh;
            model.Free();
        }
        if (mesh == null)
            return null;
        mesh = (Mesh)mesh.Duplicate();
        if (DebrisMaterial != null)
            mesh.SurfaceSetMaterial(0, DebrisMaterial);

        GpuParticles3D p = Burst(DebrisCount, DebrisLife);
        var shrink = new Curve();                   // whole until near the end, then nothing
        shrink.AddPoint(new Vector2(0, 1));
        shrink.AddPoint(new Vector2(0.72f, 1));
        shrink.AddPoint(new Vector2(1, 0));
        p.ProcessMaterial = new ParticleProcessMaterial
        {
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Point,
            Direction = Vector3.Forward,
            Spread = 180,
            Gravity = Vector3.Zero,
            InitialVelocityMin = DebrisSpeed.X * _k,
            InitialVelocityMax = DebrisSpeed.Y * _k,
            LifetimeRandomness = 1 - DebrisLife.X / DebrisLife.Y,
            ParticleFlagRotateY = false,
            AngleMin = 0,
            AngleMax = 360,
            AngularVelocityMin = -DebrisSpinDegrees,
            AngularVelocityMax = DebrisSpinDegrees,
            ScaleMin = DebrisSize.X * _k,
            ScaleMax = DebrisSize.Y * _k,
            ScaleCurve = new CurveTexture { Curve = shrink },
        };
        p.DrawPass1 = mesh;

        if (DebrisFireRate > 0)
        {
            // Each piece leaves fire behind: a sub-emitter fed by the pieces as they fly
            var fire = new GpuParticles3D
            {
                Amount = Math.Max(1, (int)Math.Ceiling(DebrisFireRate * DebrisFireLife.Y * DebrisCount)),
                Lifetime = DebrisFireLife.Y,
                LocalCoords = true,
                Emitting = false,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                VisibilityAabb = p.VisibilityAabb,
                ProcessMaterial = new ParticleProcessMaterial
                {
                    EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Point,
                    Gravity = Vector3.Zero,
                    InitialVelocityMin = 0,
                    InitialVelocityMax = 0,
                    LifetimeRandomness = 1 - DebrisFireLife.X / DebrisFireLife.Y,
                    AngleMin = 0,
                    AngleMax = 360,
                    ScaleMin = DebrisFireSize.X * _k,
                    ScaleMax = DebrisFireSize.Y * _k,
                    ScaleCurve = FireGrowth != null ? new CurveTexture { Curve = FireGrowth } : null,
                    ColorRamp = DebrisFireColors != null ? new GradientTexture1D { Gradient = DebrisFireColors } : null,
                },
                DrawPass1 = new QuadMesh { Material = Glow(FireTexture, BaseMaterial3D.BillboardModeEnum.Particles) },
            };
            AddChild(fire);
            _longest = Math.Max(_longest, DebrisLife.Y + DebrisFireLife.Y);
            ((ParticleProcessMaterial)p.ProcessMaterial).SubEmitterMode = ParticleProcessMaterial.SubEmitterModeEnum.Constant;
            ((ParticleProcessMaterial)p.ProcessMaterial).SubEmitterFrequency = DebrisFireRate;
            p.TreeEntered += () => p.SubEmitter = p.GetPathTo(fire);
        }
        return p;
    }

    /// <summary>Glowing gas: added on top, coloured by the particle.</summary>
    private static StandardMaterial3D Glow(Texture2D texture, BaseMaterial3D.BillboardModeEnum billboard) => new()
    {
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        BlendMode = BaseMaterial3D.BlendModeEnum.Add,
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        DepthDrawMode = BaseMaterial3D.DepthDrawModeEnum.Disabled,
        BillboardMode = billboard,
        BillboardKeepScale = true,
        VertexColorUseAsAlbedo = true,
        AlbedoTexture = texture,
    };
}
