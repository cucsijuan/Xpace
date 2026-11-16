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

    /// <summary>
    /// The owner's paint (<see cref="LiveryCatalog.Wear"/>), set in play only: it takes the place of Synty's atlas and of
    /// every other paint below, after the swaps. Glass, lights and other packs' materials stay as they are.
    /// </summary>
    public Material Livery { get; set; }

    private const string Atlas = "res://assets/materials/synty_atlas.tres";
    private const string PaintShader = "res://assets/materials/synty_paint.gdshader";

    private static bool Paintable(Material m) =>
        m != null && (m.ResourcePath == Atlas || m is ShaderMaterial { Shader: Shader shader } && shader.ResourcePath == PaintShader);

    /// <summary>What the nearest theme above a node makes of a material, in the colours of the nearest livery above it.</summary>
    public static Material Over(Node node, Material material)
    {
        bool swapped = false;
        Material livery = null;
        for (Node up = node.GetParent(); up != null; up = up.GetParent())
            if (up.GetNodeOrNull(NodeName) is PaintTheme theme)
            {
                if (!swapped && material != null && theme.Swaps.TryGetValue(material, out Material swap))
                {
                    material = swap;
                    swapped = true;
                }
                livery ??= theme.Livery;
            }
        return livery != null && Paintable(material) ? livery : material;
    }
}
