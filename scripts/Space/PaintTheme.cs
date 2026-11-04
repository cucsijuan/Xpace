using Godot;

namespace Xpace.Space;

/// <summary>
/// A scene's own colours: a node named <see cref="NodeName"/> whose <see cref="Swaps"/> say which material takes the
/// place of which, for every <see cref="ModelPaint"/> below its parent. The modules and panels stay as they are, one
/// scene for every station; a station painted otherwise only has this node.
/// </summary>
[Tool]
public partial class PaintTheme : Node
{
    public const string NodeName = "Theme";

    [Export] public Godot.Collections.Dictionary<Material, Material> Swaps = new();

    /// <summary>What the nearest theme above a node makes of a material.</summary>
    public static Material Over(Node node, Material material)
    {
        for (Node up = node.GetParent(); up != null; up = up.GetParent())
            if (up.GetNodeOrNull(NodeName) is PaintTheme theme && material != null && theme.Swaps.TryGetValue(material, out Material swap))
                return swap;
        return material;
    }
}
