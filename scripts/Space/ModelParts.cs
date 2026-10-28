using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Xpace.Space;

/// <summary>
/// Picks which of an imported model's meshes show: a child of the model's instance that hides every mesh not named
/// in <see cref="Parts"/>. Synty's characters come as one model holding every body, head and attachment on one
/// skeleton; a character is the few of them it wears (a body, a head, a helmet), and a character creator later only
/// changes this list. A visibility written on a node inside an imported scene is lost in an export, as a material is
/// (see <see cref="ModelPaint"/>); this node is the outer scene's own. It works in the editor too, showing every mesh
/// again while the editor saves.
/// </summary>
[Tool]
public partial class ModelParts : Node
{
    private string[] _parts = System.Array.Empty<string>();
    private readonly Dictionary<MeshInstance3D, bool> _before = new();

    /// <summary>The meshes (node names) that show; every other mesh of the model is hidden.</summary>
    [Export]
    public string[] Parts
    {
        get => _parts;
        set { _parts = value ?? System.Array.Empty<string>(); Refit(); }
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
        foreach (MeshInstance3D mesh in parent.FindChildren("*", "MeshInstance3D", true, true).OfType<MeshInstance3D>())
        {
            _before.TryAdd(mesh, mesh.Visible);
            mesh.Visible = Parts.Contains(mesh.Name.ToString());
        }
    }

    private void Unfit()
    {
        foreach (var (mesh, visible) in _before)
            if (IsInstanceValid(mesh))
                mesh.Visible = visible;
        _before.Clear();
    }
}
