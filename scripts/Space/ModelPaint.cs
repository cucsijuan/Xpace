using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Xpace.Space;

/// <summary>
/// Paints an imported model: a child of the model's instance that sets
/// <see cref="Material"/> on every mesh of that model when it starts.
/// A material_override written on a node inside an imported scene (an
/// FBX's mesh) is lost when Godot packs the scene without that node's
/// unique id, as an export does; this node is the outer scene's own, so it
/// survives. Meshes named in <see cref="ByMesh"/> get their own material
/// instead (a model's glass, say).
///
/// It paints in the editor too, so the scene shows as the game does; the
/// paint is taken off while the editor saves and put back after, so the
/// scene file never gets the overrides written on the model's nodes.
/// </summary>
[Tool]
public partial class ModelPaint : Node
{
    private Material _material;
    private Godot.Collections.Dictionary<string, Material> _byMesh = new();
    private readonly Dictionary<MeshInstance3D, Material> _before = new();

    [Export]
    public Material Material
    {
        get => _material;
        set { _material = value; Repaint(); }
    }

    [Export]
    public Godot.Collections.Dictionary<string, Material> ByMesh
    {
        get => _byMesh;
        set { _byMesh = value ?? new(); Repaint(); }
    }

    public override void _Ready() => Paint();

    public override void _Notification(int what)
    {
        if (!Engine.IsEditorHint())
            return;
        if (what == NotificationEditorPreSave)
            Unpaint();
        else if (what == NotificationEditorPostSave)
            Paint();
    }

    private void Repaint()
    {
        if (!IsInsideTree())
            return;
        Unpaint();
        Paint();
    }

    private void Paint()
    {
        if (Material == null || GetParent() is not Node parent)
            return;
        // Only the model's own meshes (they have an owner), not what scripts add under it
        foreach (MeshInstance3D mesh in parent.FindChildren("*", "MeshInstance3D", true, true).OfType<MeshInstance3D>())
        {
            _before.TryAdd(mesh, mesh.MaterialOverride);
            mesh.MaterialOverride = ByMesh.TryGetValue(mesh.Name, out Material own) ? own : Material;
        }
    }

    private void Unpaint()
    {
        foreach (var (mesh, material) in _before)
            if (IsInstanceValid(mesh))
                mesh.MaterialOverride = material;
        _before.Clear();
    }
}
