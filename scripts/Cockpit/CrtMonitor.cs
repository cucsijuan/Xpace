using Godot;

namespace Xpace.Cockpit;

/// <summary>
/// The picture tube behind a cockpit screen (assets/materials/crt_screen.gdshader): how its glass, beam and phosphor
/// show the picture. An old monitor is the same settings turned up.
/// </summary>
[GlobalClass]
public partial class CrtMonitor : Resource
{
    [Export(PropertyHint.Range, "0,3,0.01")] public float Brightness = 1.1f;

    /// <summary>How dark the gaps between the beam's lines are (they fade out when too small to see).</summary>
    [Export(PropertyHint.Range, "0,1,0.01")] public float Scanlines = 0.4f;

    /// <summary>The colour tube's aperture grille; a monochrome tube has none.</summary>
    [Export(PropertyHint.Range, "0,1,0.01")] public float Mask;

    /// <summary>Glow of bright phosphor bleeding round it.</summary>
    [Export(PropertyHint.Range, "0,2,0.01")] public float Bloom = 0.6f;

    /// <summary>The dark border round the picture, a fraction of the half screen: a bezel's lip covers none of it.</summary>
    [Export(PropertyHint.Range, "0,0.3,0.005")] public float Overscan = 0.06f;

    [Export(PropertyHint.Range, "0,0.3,0.005")] public float Curvature = 0.05f;
    [Export(PropertyHint.Range, "0,1,0.01")] public float Vignette = 0.4f;
    [Export(PropertyHint.Range, "0,0.3,0.005")] public float Flicker = 0.03f;
    [Export(PropertyHint.Range, "0,0.3,0.005")] public float Noise = 0.04f;

    /// <summary>A faint brighter band rolling down the picture.</summary>
    [Export(PropertyHint.Range, "0,0.3,0.005")] public float Roll = 0.03f;

    /// <summary>The cockpit's sheen on the glass.</summary>
    [Export(PropertyHint.Range, "0,0.3,0.005")] public float Glass = 0.04f;

    /// <summary>The tube's own colour, unlit.</summary>
    [Export] public Color Tube = new(0.015f, 0.02f, 0.018f);

    public void Apply(ShaderMaterial material)
    {
        material.SetShaderParameter("brightness", Brightness);
        material.SetShaderParameter("scanlines", Scanlines);
        material.SetShaderParameter("mask", Mask);
        material.SetShaderParameter("bloom", Bloom);
        material.SetShaderParameter("overscan", Overscan);
        material.SetShaderParameter("curvature", Curvature);
        material.SetShaderParameter("vignette", Vignette);
        material.SetShaderParameter("flicker", Flicker);
        material.SetShaderParameter("noise", Noise);
        material.SetShaderParameter("roll", Roll);
        material.SetShaderParameter("glass", Glass);
        material.SetShaderParameter("tube", Tube);
    }
}
