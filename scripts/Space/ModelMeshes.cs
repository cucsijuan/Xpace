using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Xpace.Space;

/// <summary>
/// Gives an imported model's mesh nodes other meshes: a child of the model's instance that, when it starts, sets the
/// mesh named in <see cref="Meshes"/> (by the node's name) on each of them, such as the remainder the mesh part tool
/// leaves of a model (docs/tools/mesh-parts.md). A mesh written on a node inside an imported scene is lost in an
/// export, as a material is (see <see cref="ModelPaint"/>); this node is the outer scene's own. It works in the editor
/// too, putting the model's own meshes back while the editor saves.
/// </summary>
[Tool]
public partial class ModelMeshes : Node
{
    private Godot.Collections.Dictionary<string, Mesh> _meshes = new();
    private readonly Dictionary<MeshInstance3D, Mesh> _before = new();

    [Export]
    public Godot.Collections.Dictionary<string, Mesh> Meshes
    {
        get => _meshes;
        set { _meshes = value ?? new(); Refit(); }
    }

    public override void _Ready() => Fit();

    public override void _Notification(int what)
    {
        if (!Engine.IsEditorHint())
            return;
        if (what == NotificationEditorPreSave)
            Unfit();
        else if (what == NotificationEditorPostSave)
            Fit();
    }

    private void Refit()
    {
        if (!IsInsideTree())
            return;
        Unfit();
        Fit();
    }

    private void Fit()
    {
        if (GetParent() is not Node parent)
            return;
        foreach (MeshInstance3D node in parent.FindChildren("*", "MeshInstance3D", true, true).OfType<MeshInstance3D>())
            if (Meshes.TryGetValue(node.Name, out Mesh mesh) && mesh != null)
            {
                _before.TryAdd(node, node.Mesh);
                node.Mesh = mesh;
            }
    }

    private void Unfit()
    {
        foreach (var (node, mesh) in _before)
            if (IsInstanceValid(node))
                node.Mesh = mesh;
        _before.Clear();
    }
}
