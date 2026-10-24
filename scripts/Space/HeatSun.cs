using Godot;

namespace Xpace.Space;

/// <summary>
/// The sun as a heat source: so far off that it is the same everywhere, in
/// the direction its light comes from. A seeker that looks near it sees
/// nothing else.
/// </summary>
public partial class HeatSun : DirectionalLight3D, IHeatSource
{
    [Export] public float Irradiance = 1f;     // far beyond any ship near a seeker

    public override void _Ready() => AddToGroup("heat_sources");

    public float HeatAt(Vector3 from, out Vector3 direction)
    {
        direction = GlobalBasis.Z.Normalized();    // the light shines along -Z
        return Irradiance;
    }
}
