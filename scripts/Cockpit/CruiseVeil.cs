using Godot;
using Xpace.Ship;

namespace Xpace.Cockpit;

/// <summary>
/// The thinned veil from inside: clouds of colour flowing round the edges of the
/// view (assets/materials/cruise_veil.gdshader). A sphere round the eye, outside the cockpit, so the
/// cockpit stays in front of it.
/// </summary>
public partial class CruiseVeil : MeshInstance3D
{
    [Export] public NodePath ShipPath = "../../..";
    [Export] public float Radius = 40f;

    private ShipBody _ship;
    private ShaderMaterial _material;

    public override void _Ready()
    {
        _ship = GetNodeOrNull<ShipBody>(ShipPath);
        _material = new ShaderMaterial { Shader = GD.Load<Shader>("res://assets/materials/cruise_veil.gdshader") };
        Mesh = new SphereMesh { Radius = Radius, Height = Radius * 2, RadialSegments = 48, Rings = 24 };
        MaterialOverride = _material;
        CastShadow = ShadowCastingSetting.Off;
        IgnoreOcclusionCulling = true;
        Visible = false;
    }

    public override void _Process(double delta)
    {
        if (_ship == null)
            return;
        float level = _ship.WakeLevel;
        Visible = level > 0.005f;
        if (!Visible)
            return;
        float speed = _ship.LinearVelocity.Length();
        Vector3 way = speed > 1 ? _ship.LinearVelocity / speed : -_ship.GlobalBasis.Z;
        _material.SetShaderParameter("way", way);
        _material.SetShaderParameter("level", level);
    }
}
