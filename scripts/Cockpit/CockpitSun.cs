using System;
using System.Linq;
using Godot;
using Xpace.Menu;
using Xpace.Space;

namespace Xpace.Cockpit;

/// <summary>
/// The sun, for the cockpit alone. The sun's own shadow map is laid out on a
/// grid fixed to the world, so on anything that moves its edges crawl, however
/// fine the map: a ship "at rest" still drifts some centimetres a second below
/// what its inertial unit can read. This spotlight has the sun's colour,
/// strength and direction and rides with the ship, far enough off to shine
/// almost parallel, so its map's grid stays put on the cockpit and is all spent
/// on it.
///
/// Put it in the cockpit's scene: everything below its parent goes to
/// <see cref="Layer"/>, which it lights and the sun does not. It sees only
/// <see cref="Distance"/> metres towards the sun, so it asks whether anything
/// solid or rocky further off hides the sun, and fades out if so (a station's
/// shadow, the inside of a hangar).
/// </summary>
public partial class CockpitSun : SpotLight3D
{
    /// <summary>The render layer of the cockpit (1 to 20): lit by this light, not by the sun.</summary>
    [Export(PropertyHint.Range, "2,20")] public int Layer = 2;
    /// <summary>How far towards the sun it shines from, metres: further is more parallel, nearer spends the depth better.</summary>
    [Export] public float Distance = 20f;
    /// <summary>The radius it covers round the cockpit, metres: smaller is sharper.</summary>
    [Export] public float Cover = 4f;
    [Export] public double FadeSeconds = 0.3;
    /// <summary>How often it asks whether the sun is hidden, seconds.</summary>
    [Export] public double LookEverySeconds = 0.2;

    /// <summary>The sun it stands for, in the sector the ship is in.</summary>
    public DirectionalLight3D Sun { get; private set; }

    /// <summary>Something hides the sun from the cockpit.</summary>
    public bool Hidden { get; private set; }

    private Node3D _cockpit;
    private FloatingOrigin _sector;
    private float _shown = 1;
    private double _sinceLook;

    public override void _Ready()
    {
        _cockpit = GetParent<Node3D>();
        TopLevel = true;
        LightCullMask = LayerBit;
        SpotAngle = Mathf.RadToDeg(MathF.Atan2(Cover, Distance));
        SpotRange = Distance + Cover * 3;
        SpotAttenuation = 0;                    // no falling off with distance: the sun is far off
        CallDeferred(MethodName.Claim);         // the cockpit's meshes made while it starts too
        if (GameSettings.Current is GameSettings s)
            s.Changed += ApplySettings;
        ApplySettings();
    }

    public override void _ExitTree()
    {
        if (GameSettings.Current is GameSettings s)
            s.Changed -= ApplySettings;
    }

    private uint LayerBit => 1u << (Layer - 1);

    private void Claim()
    {
        foreach (VisualInstance3D v in _cockpit.FindChildren("*", nameof(VisualInstance3D), true, false).OfType<VisualInstance3D>())
        {
            v.Layers = LayerBit;
            // The cockpit's meshes are one-sided shells: seen from behind, they must stop the light too
            if (v is GeometryInstance3D g && g.CastShadow == GeometryInstance3D.ShadowCastingSetting.On)
                g.CastShadow = GeometryInstance3D.ShadowCastingSetting.DoubleSided;
        }
    }

    private void ApplySettings() => ShadowEnabled = GameSettings.Current?.Shadows != Quality.Off;

    public override void _Process(double delta)
    {
        FindSun();
        if (Sun == null)
        {
            Visible = false;
            return;
        }
        Vector3 towards = Sun.GlobalBasis.Z.Normalized();
        Vector3 at = _cockpit.GlobalPosition;
        Vector3 up = _cockpit.GlobalBasis.Y;
        GlobalTransform = new Transform3D(MathF.Abs(towards.Dot(up.Normalized())) < 0.99f ? Basis.LookingAt(-towards, up) : Sun.GlobalBasis, at + towards * Distance);

        _sinceLook += delta;
        if (_sinceLook >= LookEverySeconds)
        {
            _sinceLook = 0;
            Hidden = Obstacles.Blocked(GetWorld3D(), at, at + towards * 100_000f);
        }
        _shown = Mathf.MoveToward(_shown, Hidden ? 0 : 1, (float)(delta / Math.Max(FadeSeconds, 0.001)));
        Visible = Sun.Visible && _shown > 0;
        LightColor = Sun.LightColor;
        LightEnergy = Sun.LightEnergy * _shown;
        LightSpecular = Sun.LightSpecular;
    }

    // The sector's sun, again if the ship went to another sector
    private void FindSun()
    {
        FloatingOrigin sector = FloatingOrigin.Of(this);
        if (sector == _sector && Sun != null && IsInstanceValid(Sun))
            return;
        _sector = sector;
        Sun = sector?.GetChildren().OfType<DirectionalLight3D>().FirstOrDefault();
        if (Sun != null)
            Sun.LightCullMask &= ~LayerBit;
    }
}
