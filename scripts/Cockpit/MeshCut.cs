using System.Collections.Generic;
using Godot;

namespace Xpace.Cockpit;

/// <summary>Takes a moving part (a lever, a button) out of a bigger mesh it was modelled into.</summary>
public static class MeshCut
{
    /// <summary>
    /// Moves the triangles of `source` that lie whole inside `box` (in its own
    /// coordinates) into a new mesh instance in the same place, and returns
    /// it; null, with a warning, if there are none.
    /// </summary>
    public static MeshInstance3D Carve(MeshInstance3D source, Aabb box, string who)
    {
        Mesh mesh = source.Mesh;
        var kept = new ArrayMesh();
        var cut = new ArrayMesh();
        for (int s = 0; s < mesh.GetSurfaceCount(); s++)
        {
            Godot.Collections.Array arrays = mesh.SurfaceGetArrays(s);
            Vector3[] vertices = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
            int[] index = arrays[(int)Mesh.ArrayType.Index].AsInt32Array();
            if (index.Length == 0)
            {
                index = new int[vertices.Length];
                for (int i = 0; i < index.Length; i++)
                    index[i] = i;
            }
            var inside = new List<int>();
            var outside = new List<int>();
            for (int i = 0; i + 2 < index.Length; i += 3)
            {
                bool part = box.HasPoint(vertices[index[i]]) && box.HasPoint(vertices[index[i + 1]])
                    && box.HasPoint(vertices[index[i + 2]]);
                (part ? inside : outside).AddRange(new[] { index[i], index[i + 1], index[i + 2] });
            }
            Material material = mesh.SurfaceGetMaterial(s);
            AddSurface(kept, arrays, outside, material);
            AddSurface(cut, arrays, inside, material);
        }
        if (cut.GetSurfaceCount() == 0)
        {
            GD.PushWarning($"{who}: no triangles of {source.Name} inside {box}");
            return null;
        }
        source.Mesh = kept;
        var piece = new MeshInstance3D { Name = source.Name + "_" + who, Mesh = cut, MaterialOverride = source.MaterialOverride };
        source.AddChild(piece);
        return piece;
    }

    private static void AddSurface(ArrayMesh mesh, Godot.Collections.Array arrays, List<int> index, Material material)
    {
        if (index.Count == 0)
            return;
        var copy = arrays.Duplicate();
        copy[(int)Mesh.ArrayType.Index] = index.ToArray();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, copy);
        mesh.SurfaceSetMaterial(mesh.GetSurfaceCount() - 1, material);
    }
}
