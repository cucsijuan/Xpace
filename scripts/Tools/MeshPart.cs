using Godot;

namespace Xpace.Tools;

/// <summary>What <see cref="MeshPartTool"/> does with a part's triangles.</summary>
public enum MeshPartMode
{
    /// <summary>Saved as its own mesh and taken out of the model.</summary>
    Extract,
    /// <summary>Saved as its own mesh; the model keeps it too.</summary>
    Copy,
    /// <summary>Only taken out of the model (something else goes there).</summary>
    Remove,
}

/// <summary>
/// One piece to take out of a bigger mesh, an entry of a
/// <see cref="MeshPartList"/>. Everything is in the coordinates of the scene
/// the pieces go into (the cockpit's, for a cockpit model), the same ones the
/// tool's survey prints.
/// </summary>
[GlobalClass]
public partial class MeshPart : Resource
{
    /// <summary>The file name of the mesh it is saved as (no extension).</summary>
    [Export] public string Name = "part";

    [Export] public MeshPartMode Mode = MeshPartMode.Extract;

    /// <summary>
    /// The triangles that lie whole inside this box are the part. A triangle
    /// goes to the first part in the list whose box holds it: put a small part
    /// sitting inside a bigger one (a knob in its track) before it.
    /// </summary>
    [Export] public Aabb Box;

    /// <summary>
    /// The part's own frame: its origin and rotation (degrees). The saved mesh
    /// is in that frame, so the part's scene can move it along its own axes;
    /// placed with this transform it is back where it was modelled. Parts that
    /// work together (a knob and its track) can share a frame.
    /// </summary>
    [Export] public Vector3 Origin;
    [Export] public Vector3 RotationDegrees;

    public Transform3D Frame => new(Basis.FromEuler(RotationDegrees * (Mathf.Pi / 180f)), Origin);
}
