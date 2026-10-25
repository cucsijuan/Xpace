using System;
using System.Collections.Generic;
using Godot;

namespace Xpace.Space;

/// <summary>
/// A missile's motor, the way Synty's FX_Missiles draws one: a flicker of
/// flame at the nozzle (FX_MuzzleFlash_01: the explosion texture, orange,
/// doubled), a bright trail left in space along the path it flew in the
/// last quarter of a second (FX_Lighting_01's line, narrowing and fading
/// to the tail: a turn bends it), and
/// puffs of smoke left in space that swell and fade in half a second
/// (FX_Missiles_Cloud: FX_MissileCloud_01, blended, turning).
///
/// Put it at the nozzle with its +Z pointing aft. The missile sets
/// <see cref="Thrust"/>; nothing burns when the motor is out (the trail
/// already left fades out).
/// </summary>
public partial class MissilePlume : Node3D, IOriginShift
{
    [Export] public Texture2D FlashTexture;                 // Explosion
    [Export] public Texture2D StreakTexture;                // Transparent_Line
    [Export] public Texture2D SmokeTexture;                 // ExplosionCloud
    [Export] public Color FlashColor = new(1f, 0.68f, 0f);
    [Export] public float FlashSize = 0.8f;                 // m, the largest flicker
    [Export] public float StreakSeconds = 0.25f;            // how long a point of the trail lasts
    [Export] public float StreakWidth = 0.3f;               // m at the nozzle (nothing at the tail)
    [Export] public float StreakStep = 0.2f;                // m, the least between two points of the trail
    [Export] public float SmokeRate = 30f;                  // puffs a second
    [Export] public float SmokeSizeMin = 0.5f;              // m
    [Export] public float SmokeSizeMax = 3f;
    [Export] public float SmokeLifeMin = 0.1f;              // s
    [Export] public float SmokeLifeMax = 0.6f;
    [Export] public float MinThrust = 0.03f;                // below this, nothing burns

    /// <summary>0 to 1: the motor's push, of the most it gives.</summary>
    public float Thrust { get; set; }

    private GpuParticles3D _flash, _smoke;
    private MeshInstance3D _streak;
    private ImmediateMesh _ribbon;
    private ShaderMaterial _streakLook;
    private readonly List<(Vector3 At, double Time)> _trail = new();
    private double _now;

    public override void _Ready()
    {
        _flash = new GpuParticles3D
        {
            Amount = 8,
            Lifetime = 0.08,
            LocalCoords = true,
            Emitting = false,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            ProcessMaterial = FlashProcess(),
            DrawPass1 = new QuadMesh { Size = Vector2.One, Material = Billboard(FlashTexture, FlashColor * 2f, true) },
        };
        AddChild(_flash);

        _smoke = new GpuParticles3D
        {
            Amount = Math.Max(1, (int)Math.Ceiling(SmokeRate * SmokeLifeMax)),
            Lifetime = SmokeLifeMax,
            LocalCoords = false,                                // left behind in space
            Emitting = false,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            VisibilityAabb = new Aabb(new Vector3(-200, -200, -200), new Vector3(400, 400, 400)),
            ProcessMaterial = SmokeProcess(),
            DrawPass1 = new QuadMesh { Size = Vector2.One, Material = Billboard(SmokeTexture, Colors.White, false) },
        };
        AddChild(_smoke);

        _ribbon = new ImmediateMesh();
        _streakLook = StreakMaterial();
        _streak = new MeshInstance3D { Mesh = _ribbon, TopLevel = true, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        AddChild(_streak);
        _streak.GlobalTransform = Transform3D.Identity;     // its points are world positions
    }

    public void ShiftOrigin(Vector3 offset)
    {
        for (int i = 0; i < _trail.Count; i++)
            _trail[i] = (_trail[i].At - offset, _trail[i].Time);
    }

    public override void _Process(double delta)
    {
        bool burning = Thrust >= MinThrust;
        if (_flash.Emitting != burning)
            _flash.Emitting = _smoke.Emitting = burning;
        _now += delta;
        Vector3 at = GlobalPosition;
        if (burning && (_trail.Count == 0 || _trail[^1].At.DistanceTo(at) >= StreakStep))
            _trail.Add((at, _now));
        _trail.RemoveAll(p => _now - p.Time > StreakSeconds);
        Ribbon(burning ? at : null);
    }

    /// <summary>
    /// The trail as a strip through the points it left (and the nozzle, while
    /// burning), turned to face the camera at each point, as wide as
    /// <see cref="StreakWidth"/> where it is newest and nothing where it ends.
    /// </summary>
    private void Ribbon(Vector3? head)
    {
        _ribbon.ClearSurfaces();
        var points = new List<(Vector3 At, double Time)>(_trail);
        if (head is Vector3 h)
            points.Add((h, _now));
        if (points.Count < 2)
            return;
        Vector3 eye = GetViewport().GetCamera3D()?.GlobalPosition ?? Vector3.Zero;
        _ribbon.SurfaceBegin(Mesh.PrimitiveType.TriangleStrip, _streakLook);
        for (int i = 0; i < points.Count; i++)
        {
            Vector3 p = points[i].At;
            Vector3 along = points[Math.Min(i + 1, points.Count - 1)].At - points[Math.Max(i - 1, 0)].At;
            Vector3 side = along.Cross(eye - p);
            side = side.LengthSquared() > 1e-8f ? side.Normalized() : Vector3.Up;
            float age = (float)Math.Clamp((_now - points[i].Time) / StreakSeconds, 0, 1);
            Vector3 half = side * (StreakWidth * (1 - age) / 2);
            _ribbon.SurfaceSetUV(new Vector2(0, age));
            _ribbon.SurfaceAddVertex(p - half);
            _ribbon.SurfaceSetUV(new Vector2(1, age));
            _ribbon.SurfaceAddVertex(p + half);
        }
        _ribbon.SurfaceEnd();
    }

    private ParticleProcessMaterial FlashProcess() => new()
    {
        EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Point,
        Gravity = Vector3.Zero,
        InitialVelocityMin = 0,
        InitialVelocityMax = 0,
        LifetimeRandomness = 0.5f,
        AngleMin = 0,
        AngleMax = 360,
        ScaleMin = FlashSize * 0.5f,
        ScaleMax = FlashSize,
    };

    private ParticleProcessMaterial SmokeProcess()
    {
        var swell = new Curve();
        swell.AddPoint(new Vector2(0, 0));
        swell.AddPoint(new Vector2(1, 1));
        var fade = new Gradient
        {
            Offsets = new[] { 0f, 0.4f, 1f },
            Colors = new[] { Colors.White with { A = 0 }, Colors.White, Colors.White with { A = 0 } },
        };
        return new ParticleProcessMaterial
        {
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box,
            EmissionBoxExtents = new Vector3(0.5f, 0.25f, 0.05f),
            Gravity = Vector3.Zero,
            InitialVelocityMin = 0,
            InitialVelocityMax = 0,
            LifetimeRandomness = 1 - SmokeLifeMin / SmokeLifeMax,
            AngleMin = 0,
            AngleMax = 360,
            AngularVelocityMin = -45,
            AngularVelocityMax = 45,
            ScaleMin = SmokeSizeMin,
            ScaleMax = SmokeSizeMax,
            ScaleCurve = new CurveTexture { Curve = swell },
            ColorRamp = new GradientTexture1D { Gradient = fade },
        };
    }

    /// <summary>A camera-facing particle: added on top (flame) or blended (smoke).</summary>
    private static StandardMaterial3D Billboard(Texture2D texture, Color color, bool add) => new()
    {
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        BlendMode = add ? BaseMaterial3D.BlendModeEnum.Add : BaseMaterial3D.BlendModeEnum.Mix,
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        DepthDrawMode = BaseMaterial3D.DepthDrawModeEnum.Disabled,
        BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles,
        BillboardKeepScale = true,
        VertexColorUseAsAlbedo = true,
        AlbedoTexture = texture,
        AlbedoColor = color,
    };

    /// <summary>The trail: the line texture across, bright where newest, nothing at the tail.</summary>
    private ShaderMaterial StreakMaterial()
    {
        var shader = new Shader
        {
            Code = """
                shader_type spatial;
                render_mode unshaded, blend_add, depth_draw_never, cull_disabled;
                uniform sampler2D line : source_color, hint_default_white;
                void fragment() {
                    // u runs across the strip, v from the newest point (0) to the oldest (1);
                    // the line texture runs along its u, so across the strip is its v
                    vec4 t = texture(line, vec2(0.5, UV.x));
                    float fade = 1.0 - UV.y;
                    ALBEDO = vec3(2.0) * t.rgb;
                    ALPHA = t.a * fade;
                }
                """,
        };
        var material = new ShaderMaterial { Shader = shader };
        material.SetShaderParameter("line", StreakTexture);
        return material;
    }
}
