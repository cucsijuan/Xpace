using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Xpace.Space;

/// <summary>
/// Smooths the normals of an imported model's meshes: a child of the model's instance (as <see cref="ModelPaint"/>,
/// so it survives an export) that, when it starts, gives the meshes named in <see cref="Meshes"/> (every mesh if
/// empty) a copy whose normals are averaged over the faces meeting at each corner. Low-poly glass is flat-shaded:
/// each facet reflects the sky at its own angle and the canopy shows wedges; smoothed, the reflections flow across.
/// Faces meeting at more than <see cref="Angle"/> keep their edge. Works in the editor too, putting the model's own
/// meshes back while the editor saves (as <see cref="ModelPaint"/> does with its paint).
/// </summary>
[Tool]
public partial class SmoothNormals : Node
{
    [Export] public string[] Meshes = System.Array.Empty<string>();
    [Export(PropertyHint.Range, "0,180")] public float Angle = 60;

    private readonly Dictionary<MeshInstance3D, Mesh> _before = new();

    public override void _Ready() => Smooth();

    public override void _Notification(int what)
    {
        if (!Engine.IsEditorHint())
            return;
        if (what == NotificationEditorPreSave)
            Restore();
        else if (what == NotificationEditorPostSave)
            Smooth();
    }

    private void Smooth()
    {
        if (GetParent() is not Node parent)
            return;
        foreach (MeshInstance3D mesh in parent.FindChildren("*", "MeshInstance3D", true, true).OfType<MeshInstance3D>())
            if ((Meshes.Length == 0 || Meshes.Contains(mesh.Name.ToString())) && mesh.Mesh is Mesh m && !_before.ContainsKey(mesh))
            {
                _before[mesh] = m;
                mesh.Mesh = Smoothed(m, Mathf.Cos(Mathf.DegToRad(Angle)));
            }
    }

    private void Restore()
    {
        foreach (var (mesh, original) in _before)
            if (IsInstanceValid(mesh))
                mesh.Mesh = original;
        _before.Clear();
    }

    private static ArrayMesh Smoothed(Mesh source, float cosLimit)
    {
        var result = new ArrayMesh();
        for (int s = 0; s < source.GetSurfaceCount(); s++)
        {
            var arrays = source.SurfaceGetArrays(s);
            Vector3[] v = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
            int[] index = arrays[(int)Mesh.ArrayType.Index].AsInt32Array();
            if (index.Length == 0)
                index = Enumerable.Range(0, v.Length).ToArray();

            // Each face's normal (area weighted) and, per corner position, the faces touching it
            var faces = new Vector3[index.Length / 3];
            var at = new Dictionary<Vector3, List<int>>();
            for (int f = 0; f < faces.Length; f++)
            {
                Vector3 a = v[index[3 * f]], b = v[index[3 * f + 1]], c = v[index[3 * f + 2]];
                faces[f] = (c - a).Cross(b - a);      // Godot winds clockwise seen from the front
                for (int k = 0; k < 3; k++)
                {
                    Vector3 key = v[index[3 * f + k]].Snapped(Vector3.One * 1e-4f);
                    if (!at.TryGetValue(key, out var list))
                        at[key] = list = new List<int>();
                    list.Add(f);
                }
            }

            Vector3[] normals = arrays[(int)Mesh.ArrayType.Normal].AsVector3Array();
            if (normals.Length != v.Length)
                normals = new Vector3[v.Length];
            for (int f = 0; f < faces.Length; f++)
            {
                Vector3 own = faces[f].Normalized();
                for (int k = 0; k < 3; k++)
                {
                    int i = index[3 * f + k];
                    Vector3 sum = Vector3.Zero;
                    foreach (int g in at[v[i].Snapped(Vector3.One * 1e-4f)])
                        if (faces[g].Normalized().Dot(own) >= cosLimit)
                            sum += faces[g];
                    // Keep the side the model's own normal faces
                    Vector3 n = sum.Normalized();
                    normals[i] = normals[i] != Vector3.Zero && n.Dot(normals[i]) < 0 ? -n : n;
                }
            }
            arrays[(int)Mesh.ArrayType.Normal] = normals;
            arrays[(int)Mesh.ArrayType.Tangent] = default;
            result.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
            result.SurfaceSetMaterial(s, source.SurfaceGetMaterial(s));
        }
        return result;
    }
}
