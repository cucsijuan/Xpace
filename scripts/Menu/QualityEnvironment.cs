using System.Collections.Generic;
using Godot;

namespace Xpace.Menu;

/// <summary>
/// A WorldEnvironment that follows the player's graphics settings. The scene's designer turns effects on in the
/// Environment (ambient occlusion, indirect light, reflections, glow) and shadows on the directional lights beside
/// this node; the settings can only take away what the designer gave, never add an effect the scene does not have.
/// </summary>
public partial class QualityEnvironment : WorldEnvironment
{
    private bool _ssao, _ssil, _ssr, _glow;
    private readonly List<(DirectionalLight3D Light, bool Shadow)> _lights = new();

    public override void _Ready()
    {
        if (Environment is Environment e)
        {
            _ssao = e.SsaoEnabled;
            _ssil = e.SsilEnabled;
            _ssr = e.SsrEnabled;
            _glow = e.GlowEnabled;
        }
        foreach (Node sibling in GetParent().GetChildren())
            if (sibling is DirectionalLight3D light)
                _lights.Add((light, light.ShadowEnabled));
        if (GameSettings.Current is GameSettings s)
            s.Changed += Apply;
        Apply();
    }

    public override void _ExitTree()
    {
        if (GameSettings.Current is GameSettings s)
            s.Changed -= Apply;
    }

    private void Apply()
    {
        GameSettings s = GameSettings.Current;
        if (s == null)
            return;
        if (Environment is Environment e)
        {
            e.SsaoEnabled = _ssao && s.AmbientOcclusion != Quality.Off;
            e.SsilEnabled = _ssil && s.IndirectLight != Quality.Off;
            e.SsrEnabled = _ssr && s.Reflections;
            e.GlowEnabled = _glow && s.Glow;
        }
        foreach (var (light, shadow) in _lights)
            if (IsInstanceValid(light))
            {
                light.ShadowEnabled = shadow && s.Shadows != Quality.Off;
                light.DirectionalShadowMode = s.Shadows == Quality.Low
                    ? DirectionalLight3D.ShadowMode.Parallel2Splits : DirectionalLight3D.ShadowMode.Parallel4Splits;
            }
    }
}
