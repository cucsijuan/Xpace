using Godot;

namespace Xpace.Tools;

/// <summary>
/// A list of pieces to take out of one mesh of a model, for
/// <see cref="MeshPartTool"/>. See docs/tools/mesh-parts.md.
/// </summary>
[GlobalClass]
public partial class MeshPartList : Resource
{
    /// <summary>The model (an imported FBX, glTF... or a scene).</summary>
    [Export] public PackedScene Source;

    /// <summary>The mesh to cut, a path inside the model.</summary>
    [Export] public NodePath SourceMesh = "";

    /// <summary>
    /// Where the model sits in the scene the pieces go into (the cockpit
    /// rotates its model half a turn: Synty's face +Z). Boxes, origins and the
    /// report are in that scene's coordinates.
    /// </summary>
    [Export] public Transform3D Placement = Transform3D.Identity;

    /// <summary>Where the meshes and the report are written (keep licensed art out of git).</summary>
    [Export(PropertyHint.Dir)] public string OutputFolder = "res://assets/synty/parts";

    /// <summary>The model's mesh without the extracted and removed parts ("" to skip it).</summary>
    [Export] public string RemainderName = "remainder";

    [Export] public Godot.Collections.Array<MeshPart> Parts = new();

    /// <summary>Survey: the box (scene coordinates) whose loose pieces the survey lists.</summary>
    [Export] public Aabb SurveyBox;
}
