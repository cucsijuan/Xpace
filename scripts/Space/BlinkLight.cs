using Godot;

namespace Xpace.Space;

/// <summary>
/// A light that blinks, for a beacon or a mast: a glow that faces the eye and a small lamp, both coming up and going
/// out once every <see cref="PeriodSeconds"/>. Put the node where the light is. Lights of the same period with
/// different <see cref="Phase"/> take turns.
/// </summary>
public partial class BlinkLight : Node3D
{
    [Export] public Texture2D Glow;
    [Export] public Color Colour = new(0.2f, 1f, 0.8f);
    [Export] public float Size = 6f;                    // the glow across (m)
    [Export] public float PeriodSeconds = 2f;
    [Export(PropertyHint.Range, "0,1")] public float OnShare = 0.25f;   // of the period
    [Export(PropertyHint.Range, "0,1")] public float Phase = 0f;        // of the period
    [Export] public float LightEnergy = 3f;
    [Export] public float LightRange = 30f;

    /// <summary>How lit it is now, 0 to 1.</summary>
    public float Lit { get; private set; }

    private Sprite3D _glow;
    private OmniLight3D _lamp;

    public override void _Ready()
    {
        _glow = new Sprite3D
        {
            Texture = Glow,
            PixelSize = Glow != null ? Size / Glow.GetWidth() : 0.01f,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            Shaded = false,
            Transparent = true,
            AlphaCut = SpriteBase3D.AlphaCutMode.Disabled,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Modulate = Colour,
        };
        _glow.MaterialOverride = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            BlendMode = BaseMaterial3D.BlendModeEnum.Add,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled,
            BillboardKeepScale = true,
            AlbedoTexture = Glow,
            VertexColorUseAsAlbedo = true,
        };
        AddChild(_glow);
        _lamp = new OmniLight3D { LightColor = Colour, OmniRange = LightRange, LightEnergy = 0 };
        AddChild(_lamp);
    }

    public override void _Process(double delta)
    {
        double at = Time.GetTicksMsec() / 1000.0 / PeriodSeconds + Phase;
        float t = (float)(at - Mathf.Floor(at)) / Mathf.Max(OnShare, 0.01f);
        Lit = t < 1 ? Mathf.Sin(t * Mathf.Pi) : 0;
        _glow.Transparency = 1 - Lit;               // not Modulate: that redraws the sprite, even in a sector left out
        _lamp.LightEnergy = LightEnergy * Lit;
    }
}
