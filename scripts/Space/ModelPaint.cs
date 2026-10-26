using System.Linq;
using Godot;

namespace Xpace.Space;

/// <summary>
/// Paints an imported model: a child of the model's instance that sets
/// <see cref="Material"/> on every mesh of that model when it starts.
/// A material_override written on a node inside an imported scene (an
/// FBX's mesh) is lost when Godot packs the scene without that node's
/// unique id, as an export does; this node is the outer scene's own, so it
/// survives.
/// </summary>
public partial class ModelPaint : Node
{
    [Export] public Material Material;

    public override void _Ready()
    {
        if (Material == null || GetParent() is not Node parent)
            return;
        // Only the model's own meshes (they have an owner), not what scripts add under it
        foreach (MeshInstance3D mesh in parent.FindChildren("*", "MeshInstance3D", true, true).OfType<MeshInstance3D>())
            mesh.MaterialOverride = Material;
    }
}
