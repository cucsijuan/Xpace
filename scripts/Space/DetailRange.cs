using System.Linq;
using Godot;

namespace Xpace.Space;

/// <summary>
/// What is not worth drawing from afar: everything under this node's parent
/// goes, picture and shadows, past <see cref="Range"/> metres from the camera,
/// except the nodes named in <see cref="Keep"/> and what is under them. A
/// hangar keeps what shows from outside (its door, its casing) and loses its
/// inside, by far most of its triangles. New things put in the scene are
/// covered without further ado.
///
/// Meshes whose names end in <see cref="SmallEnding"/> (tools/station_bay/lods.py
/// makes them: bolts, brackets) go much sooner, at <see cref="SmallRange"/>,
/// wherever they are, those of a kept node too, and cast no shadows.
/// </summary>
public partial class DetailRange : Node
{
    [Export] public float Range = 1000f;
    [Export] public string[] Keep = [];
    [Export] public string SmallEnding = "_small";
    [Export] public float SmallRange = 60f;

    private const float LightFade = 100f;

    // Deferred: the scene's other nodes may still be putting their meshes in place
    public override void _Ready() => Callable.From(() => Apply(GetParent(), true)).CallDeferred();

    private void Apply(Node node, bool top)
    {
        if (!top)
        {
            if (node is GeometryInstance3D part && node.Name.ToString().EndsWith(SmallEnding))
            {
                part.VisibilityRangeEnd = SmallRange;
                part.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
                return;
            }
            if (Keep.Contains(node.Name.ToString()))
                return;
            if (node is GeometryInstance3D geometry)
                geometry.VisibilityRangeEnd = Range;
            else if (node is Light3D light)
            {
                light.DistanceFadeEnabled = true;
                light.DistanceFadeBegin = Range - LightFade;
                light.DistanceFadeLength = LightFade;
            }
        }
        foreach (Node child in node.GetChildren())
            Apply(child, false);
    }
}
