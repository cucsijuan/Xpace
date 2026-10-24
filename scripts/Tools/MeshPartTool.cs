using System;
using System.Collections.Generic;
using System.Text;
using Godot;

namespace Xpace.Tools;

/// <summary>
/// Takes pieces out of a model's mesh so they can be scenes of their own (a
/// slider, a switch, a button), following a <see cref="MeshPartList"/>.
/// Two jobs:
///
///   survey    lists the loose pieces (connected triangles) inside the list's
///             SurveyBox, with the box and centre of each: what to put in the
///             list;
///   extract   saves every part as a mesh in its own frame, the model's mesh
///             without them, and a report with each part's transform in the
///             scene (to place its scene where it was).
///
/// Run tools/mesh_parts/mesh_part_tool.tscn from the editor (F6) with
/// List and Job set on the node, or from the command line:
///
///   godot --headless --path . res://tools/mesh_parts/mesh_part_tool.tscn -- LIST.tres extract
///   godot --headless --path . res://tools/mesh_parts/mesh_part_tool.tscn -- LIST.tres survey [x y z sx sy sz]
///
/// See docs/tools/mesh-parts.md.
/// </summary>
public partial class MeshPartTool : Node
{
    public enum ToolJob { Extract, Survey }

    [Export] public MeshPartList List;
    [Export] public ToolJob Job = ToolJob.Extract;

    private readonly StringBuilder _report = new();

    public override void _Ready()
    {
        string[] args = OS.GetCmdlineUserArgs();
        if (args.Length > 0)
        {
            List = GD.Load<MeshPartList>(args[0]);
            Job = args.Length > 1 && args[1] == "survey" ? ToolJob.Survey : ToolJob.Extract;
            if (List != null && Job == ToolJob.Survey && args.Length >= 8)
                List.SurveyBox = new Aabb(
                    new Vector3(float.Parse(args[2]), float.Parse(args[3]), float.Parse(args[4])),
                    new Vector3(float.Parse(args[5]), float.Parse(args[6]), float.Parse(args[7])));
        }
        int failures = Run() ? 0 : 1;
        GetTree().Quit(failures);
    }

    private bool Run()
    {
        if (List?.Source == null)
        {
            GD.PrintErr("MeshPartTool: no list, or the list has no Source (usage: -- LIST.tres extract|survey)");
            return false;
        }
        Node model = List.Source.Instantiate();
        var source = model.GetNodeOrNull<MeshInstance3D>(List.SourceMesh);
        if (source?.Mesh == null)
        {
            GD.PrintErr($"MeshPartTool: no mesh at {List.SourceMesh} in {List.Source.ResourcePath}");
            model.Free();
            return false;
        }
        // From the mesh's own coordinates to the scene's
        Transform3D toScene = Transform3D.Identity;
        for (Node n = source; n != model && n is Node3D n3; n = n.GetParent())
            toScene = n3.Transform * toScene;
        toScene = List.Placement * toScene;

        bool ok = Job == ToolJob.Survey ? Survey(source.Mesh, toScene) : Extract(source.Mesh, toScene);
        model.Free();
        return ok;
    }

    // ---------- survey ----------

    private bool Survey(Mesh mesh, Transform3D toScene)
    {
        GD.Print($"Loose pieces inside {GD.VarToStr(List.SurveyBox)}:");
        int count = 0;
        for (int s = 0; s < mesh.GetSurfaceCount(); s++)
        {
            (Vector3[] vertices, int[] index) = Geometry(mesh, s);
            var inside = new List<int>();
            for (int t = 0; t + 2 < index.Length; t += 3)
                if (Inside(List.SurveyBox, toScene, vertices, index, t))
                    inside.Add(t);
            foreach (List<int> piece in Islands(vertices, index, inside))
            {
                Aabb box = Bounds(piece, t => toScene * vertices[index[t]], index);
                GD.Print($"  surface {s}, {piece.Count / 1,4} triangles  box {GD.VarToStr(box.Grow(0.0005f))}  centre {GD.VarToStr(box.GetCenter())}");
                count++;
            }
        }
        GD.Print($"{count} pieces.");
        return true;
    }

    // Triangles that share a corner (by position: models often split vertices
    // along seams) belong to the same piece
    private static List<List<int>> Islands(Vector3[] vertices, int[] index, List<int> triangles)
    {
        var parent = new Dictionary<Vector3I, Vector3I>();
        Vector3I Key(Vector3 v) => new((int)MathF.Round(v.X * 10000), (int)MathF.Round(v.Y * 10000), (int)MathF.Round(v.Z * 10000));
        Vector3I Find(Vector3I k)
        {
            while (parent[k] != k)
                k = parent[k] = parent[parent[k]];
            return k;
        }
        foreach (int t in triangles)
        {
            for (int k = 0; k < 3; k++)
                parent.TryAdd(Key(vertices[index[t + k]]), Key(vertices[index[t + k]]));
            Vector3I root = Find(Key(vertices[index[t]]));
            for (int k = 1; k < 3; k++)
            {
                Vector3I other = Find(Key(vertices[index[t + k]]));
                if (other != root)
                    parent[other] = root;
            }
        }
        var pieces = new Dictionary<Vector3I, List<int>>();
        foreach (int t in triangles)
        {
            Vector3I root = Find(Key(vertices[index[t]]));
            if (!pieces.TryGetValue(root, out List<int> list))
                pieces[root] = list = new List<int>();
            list.Add(t);
        }
        return new List<List<int>>(pieces.Values);
    }

    // ---------- extract ----------

    private bool Extract(Mesh mesh, Transform3D toScene)
    {
        string folder = List.OutputFolder.TrimEnd('/');
        DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath(folder));
        _report.AppendLine($"Mesh parts of {List.Source.ResourcePath} : {List.SourceMesh}, from {List.ResourcePath}");
        _report.AppendLine("Each part's transform places its mesh back where it was modelled, in the scene the list is for.");
        _report.AppendLine();

        int parts = List.Parts.Count;
        var meshes = new ArrayMesh[parts];
        var bounds = new Aabb?[parts];
        var triangles = new int[parts];
        var remainder = new ArrayMesh();
        bool ok = true;

        for (int s = 0; s < mesh.GetSurfaceCount(); s++)
        {
            Godot.Collections.Array arrays = mesh.SurfaceGetArrays(s);
            (Vector3[] vertices, int[] index) = Geometry(mesh, s);
            Material material = mesh.SurfaceGetMaterial(s);

            // Each triangle goes to the first part whose box holds it
            var claimed = new List<int>[parts];
            var kept = new List<int>();
            for (int t = 0; t + 2 < index.Length; t += 3)
            {
                int owner = -1;
                for (int p = 0; p < parts && owner < 0; p++)
                    if (Inside(List.Parts[p].Box, toScene, vertices, index, t))
                        owner = p;
                if (owner >= 0)
                    (claimed[owner] ??= new List<int>()).Add(t);
                if (owner < 0 || List.Parts[owner].Mode == MeshPartMode.Copy)
                    kept.AddRange(new[] { index[t], index[t + 1], index[t + 2] });
            }

            for (int p = 0; p < parts; p++)
            {
                MeshPart part = List.Parts[p];
                if (claimed[p] == null || part.Mode == MeshPartMode.Remove)
                {
                    triangles[p] += claimed[p]?.Count ?? 0;
                    continue;
                }
                Transform3D toPart = part.Frame.AffineInverse() * toScene;
                meshes[p] ??= new ArrayMesh();
                AddPiece(meshes[p], arrays, index, claimed[p], toPart, material);
                Aabb box = Bounds(claimed[p], t => toPart * vertices[index[t]], index);
                bounds[p] = bounds[p]?.Merge(box) ?? box;
                triangles[p] += claimed[p].Count;
            }
            if (kept.Count > 0)
            {
                Godot.Collections.Array copy = arrays.Duplicate();
                copy[(int)Mesh.ArrayType.Index] = kept.ToArray();
                remainder.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, copy);
                remainder.SurfaceSetMaterial(remainder.GetSurfaceCount() - 1, material);
            }
        }

        for (int p = 0; p < parts; p++)
        {
            MeshPart part = List.Parts[p];
            if (triangles[p] == 0)
            {
                GD.PushWarning($"MeshPartTool: part {part.Name} has no triangles inside {GD.VarToStr(part.Box)}");
                _report.AppendLine($"{part.Name}: NO TRIANGLES inside {GD.VarToStr(part.Box)}");
                ok = false;
                continue;
            }
            if (part.Mode == MeshPartMode.Remove)
            {
                _report.AppendLine($"{part.Name}: {triangles[p]} triangles removed");
                continue;
            }
            string path = $"{folder}/{part.Name}.res";
            ok &= Save(meshes[p], path);
            _report.AppendLine($"{part.Name}: {triangles[p]} triangles -> {path}");
            _report.AppendLine($"  transform  {GD.VarToStr(part.Frame)}");
            _report.AppendLine($"  bounds in its frame  {GD.VarToStr(bounds[p].Value)}");
        }
        if (!string.IsNullOrEmpty(List.RemainderName))
        {
            string path = $"{folder}/{List.RemainderName}.res";
            ok &= Save(remainder, path);
            _report.AppendLine($"the rest -> {path} (in the model's own coordinates: give it to the model's mesh node)");
        }

        string reportPath = $"{folder}/{List.ResourcePath.GetFile().GetBaseName()}_report.txt";
        using (var file = Godot.FileAccess.Open(reportPath, Godot.FileAccess.ModeFlags.Write))
            file?.StoreString(_report.ToString());
        GD.Print(_report.ToString());
        GD.Print($"Report: {reportPath}");
        return ok;
    }

    private static bool Save(Resource resource, string path)
    {
        Error e = ResourceSaver.Save(resource, path);
        if (e != Error.Ok)
            GD.PrintErr($"MeshPartTool: cannot save {path}: {e}");
        return e == Error.Ok;
    }

    /// <summary>
    /// A surface made of some of a source surface's triangles: only the
    /// vertices they use, moved into the part's frame (positions, normals,
    /// tangents); colours and UVs as they were.
    /// </summary>
    private static void AddPiece(ArrayMesh into, Godot.Collections.Array arrays, int[] index, List<int> triangles,
                                 Transform3D toPart, Material material)
    {
        var map = new Dictionary<int, int>();
        var newIndex = new int[triangles.Count * 3];
        int n = 0;
        foreach (int t in triangles)
            for (int k = 0; k < 3; k++)
            {
                int old = index[t + k];
                if (!map.TryGetValue(old, out int fresh))
                    map[old] = fresh = map.Count;
                newIndex[n++] = fresh;
            }
        var order = new int[map.Count];
        foreach ((int old, int fresh) in map)
            order[fresh] = old;

        Basis normals = toPart.Basis.Inverse().Transposed();
        var result = new Godot.Collections.Array();
        result.Resize((int)Mesh.ArrayType.Max);
        Vector3[] vertices = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
        result[(int)Mesh.ArrayType.Vertex] = Pick(order, i => toPart * vertices[i]);
        if (arrays[(int)Mesh.ArrayType.Normal].VariantType != Variant.Type.Nil)
        {
            Vector3[] normal = arrays[(int)Mesh.ArrayType.Normal].AsVector3Array();
            result[(int)Mesh.ArrayType.Normal] = Pick(order, i => (normals * normal[i]).Normalized());
        }
        if (arrays[(int)Mesh.ArrayType.Tangent].VariantType != Variant.Type.Nil)
        {
            float[] tangent = arrays[(int)Mesh.ArrayType.Tangent].AsFloat32Array();
            var moved = new float[order.Length * 4];
            for (int i = 0; i < order.Length; i++)
            {
                Vector3 t = (toPart.Basis * new Vector3(tangent[order[i] * 4], tangent[order[i] * 4 + 1], tangent[order[i] * 4 + 2])).Normalized();
                moved[i * 4] = t.X;
                moved[i * 4 + 1] = t.Y;
                moved[i * 4 + 2] = t.Z;
                moved[i * 4 + 3] = tangent[order[i] * 4 + 3];
            }
            result[(int)Mesh.ArrayType.Tangent] = moved;
        }
        if (arrays[(int)Mesh.ArrayType.Color].VariantType != Variant.Type.Nil)
        {
            Color[] color = arrays[(int)Mesh.ArrayType.Color].AsColorArray();
            result[(int)Mesh.ArrayType.Color] = Pick(order, i => color[i]);
        }
        foreach (Mesh.ArrayType uvType in new[] { Mesh.ArrayType.TexUV, Mesh.ArrayType.TexUV2 })
            if (arrays[(int)uvType].VariantType != Variant.Type.Nil)
            {
                Vector2[] uv = arrays[(int)uvType].AsVector2Array();
                result[(int)uvType] = Pick(order, i => uv[i]);
            }
        result[(int)Mesh.ArrayType.Index] = newIndex;
        into.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, result);
        into.SurfaceSetMaterial(into.GetSurfaceCount() - 1, material);
    }

    private static T[] Pick<T>(int[] order, Func<int, T> value)
    {
        var a = new T[order.Length];
        for (int i = 0; i < order.Length; i++)
            a[i] = value(order[i]);
        return a;
    }

    // ---------- shared ----------

    private static (Vector3[], int[]) Geometry(Mesh mesh, int surface)
    {
        Godot.Collections.Array arrays = mesh.SurfaceGetArrays(surface);
        Vector3[] vertices = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
        int[] index = arrays[(int)Mesh.ArrayType.Index].AsInt32Array();
        if (index.Length == 0)
        {
            index = new int[vertices.Length];
            for (int i = 0; i < index.Length; i++)
                index[i] = i;
        }
        return (vertices, index);
    }

    private static bool Inside(Aabb box, Transform3D toScene, Vector3[] vertices, int[] index, int t) =>
        box.HasPoint(toScene * vertices[index[t]]) && box.HasPoint(toScene * vertices[index[t + 1]])
        && box.HasPoint(toScene * vertices[index[t + 2]]);

    private static Aabb Bounds(List<int> triangles, Func<int, Vector3> corner, int[] index)
    {
        var box = new Aabb(corner(triangles[0]), Vector3.Zero);
        foreach (int t in triangles)
            for (int k = 0; k < 3; k++)
                box = box.Expand(corner(t + k));
        return box;
    }
}
