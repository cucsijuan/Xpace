using Godot;

namespace Xpace.Space;

/// <summary>
/// How a sun's shadow map is spent, in metres from the camera: four cascades,
/// each ending where this says. A cascade's texels are as wide as its far end
/// over the map's resolution, so the ratio from one to the next is how much
/// coarser than the picture the shadow can get at the start of a cascade, and
/// <see cref="Max"/> is where shadows have faded out altogether.
/// </summary>
[GlobalClass]
public partial class ShadowReach : Resource
{
    [Export] public float Near = 60f;
    [Export] public float Middle = 300f;
    [Export] public float Far = 1500f;
    [Export] public float Max = 7500f;
    /// <summary>How far the edges are smoothed; the far cascades, being coarser, smooth over more metres. Much above 2 turns to grain on detailed hulls.</summary>
    [Export] public float Blur = 2f;
}
