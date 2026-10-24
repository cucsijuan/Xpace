using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Xpace.Tools;

/// <summary>
/// Makes a ship's hitboxes from a collision mesh: the mesh is cut into its
/// loose pieces (connected triangles; a model's collision mesh is usually a
/// set of convex blocks), and every piece gets the smallest box, turned as
/// the piece is, that holds it. The boxes are saved as a scene of
/// CollisionShape3D nodes, in the model's frame, to put beside the model in
/// a ship's scene (rounds and blasts hit them, see Net.Hitbox).
///
///   godot --headless --path . res://tools/hitboxes/hitbox_tool.tscn -- COLLISION.fbx OUT.tscn
///
/// The FBX is read from anywhere (it need not be imported). A box is
/// tried along the piece's principal axes and along each of its faces with
/// that face's longest edge; the one with the least volume wins (a block's
/// faces give its exact box). See docs/tools/hitboxes.md.
/// </summary>
public partial class HitboxTool : Node
{
    [Export(PropertyHint.GlobalFile, "*.fbx")] public string From;
    [Export(PropertyHint.SaveFile, "*.tscn")] public string To;

    /// <summary>The thinnest a box is made, m (a flat piece still has some).</summary>
    [Export] public float MinThickness = 0.1f;

    public override void _Ready()
    {
        string[] args = OS.GetCmdlineUserArgs();
        if (args.Length >= 2)
        {
            From = args[0];
            To = args[1];
        }
        GetTree().Quit(Run() ? 0 : 1);
    }

    private bool Run()
    {
        if (string.IsNullOrEmpty(From) || string.IsNullOrEmpty(To))
        {
            GD.PrintErr("HitboxTool: usage: -- COLLISION.fbx OUT.tscn");
            return false;
        }
        var doc = new FbxDocument();
        var state = new FbxState();
        if (doc.AppendFromFile(From, state) != Error.Ok || doc.GenerateScene(state) is not Node model)
        {
            GD.PrintErr($"HitboxTool: cannot read {From}");
            return false;
        }

        var triangles = new List<Vector3[]>();
        foreach (Node n in model.FindChildren("*", nameof(MeshInstance3D), true, false))
        {
            var m = (MeshInstance3D)n;
            Transform3D toModel = Frame(model, m);
            for (int s = 0; s < m.Mesh.GetSurfaceCount(); s++)
            {
                var arrays = m.Mesh.SurfaceGetArrays(s);
                Vector3[] v = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
                int[] index = arrays[(int)Mesh.ArrayType.Index].AsInt32Array();
                if (index.Length == 0)
                    index = Enumerable.Range(0, v.Length).ToArray();
                for (int i = 0; i + 2 < index.Length; i += 3)
                    triangles.Add(new[] { toModel * v[index[i]], toModel * v[index[i + 1]], toModel * v[index[i + 2]] });
            }
        }
        model.Free();

        var root = new Node3D { Name = "Hitboxes" };
        int count = 0;
        foreach (List<Vector3[]> piece in Pieces(triangles))
        {
            (Basis turn, Vector3 centre, Vector3 size) = Fit(piece);
            var shape = new CollisionShape3D
            {
                Name = $"Box{++count}",
                Shape = new BoxShape3D { Size = size },
                Transform = new Transform3D(turn, centre),
            };
            root.AddChild(shape);
            shape.Owner = root;
            GD.Print($"box {count}: centre {centre}, size {size}");
        }
        var scene = new PackedScene();
        scene.Pack(root);
        Error saved = ResourceSaver.Save(scene, To);
        root.Free();
        GD.Print(saved == Error.Ok ? $"{count} boxes to {To}" : $"HitboxTool: cannot save {To}: {saved}");
        return saved == Error.Ok && count > 0;
    }

    /// <summary>A node's transform in the model's frame.</summary>
    private static Transform3D Frame(Node model, Node3D n)
    {
        Transform3D t = Transform3D.Identity;
        for (Node p = n; p != null && p != model; p = p.GetParent())
            if (p is Node3D p3)
                t = p3.Transform * t;
        return t;
    }

    /// <summary>The loose pieces: triangles sharing a corner (by position) are one piece.</summary>
    private static IEnumerable<List<Vector3[]>> Pieces(List<Vector3[]> triangles)
    {
        var parent = Enumerable.Range(0, triangles.Count).ToArray();
        int Find(int i) => parent[i] == i ? i : parent[i] = Find(parent[i]);
        var corner = new Dictionary<(long, long, long), int>();
        for (int t = 0; t < triangles.Count; t++)
            foreach (Vector3 p in triangles[t])
            {
                var key = ((long)Math.Round(p.X * 1000), (long)Math.Round(p.Y * 1000), (long)Math.Round(p.Z * 1000));
                if (corner.TryGetValue(key, out int other))
                    parent[Find(t)] = Find(other);
                else
                    corner[key] = t;
            }
        return Enumerable.Range(0, triangles.Count).GroupBy(Find).Select(g => g.Select(i => triangles[i]).ToList());
    }

    /// <summary>The least-volume box over the candidate frames.</summary>
    private (Basis, Vector3, Vector3) Fit(List<Vector3[]> piece)
    {
        Vector3[] points = piece.SelectMany(t => t).ToArray();
        var frames = new List<Basis> { Basis.Identity, Principal(points) };
        foreach (Vector3[] t in piece)
        {
            Vector3 n = (t[1] - t[0]).Cross(t[2] - t[0]);
            if (n.LengthSquared() < 1e-10f)
                continue;
            n = n.Normalized();
            Vector3 edge = new[] { t[1] - t[0], t[2] - t[1], t[0] - t[2] }.OrderByDescending(e => e.LengthSquared()).First();
            Vector3 u = (edge - n * edge.Dot(n)).Normalized();
            frames.Add(new Basis(u, n.Cross(u), n));
        }
        float best = float.MaxValue;
        (Basis, Vector3, Vector3) fit = default;
        foreach (Basis b in frames)
        {
            Basis inv = b.Inverse();
            Vector3 lo = Vector3.One * float.MaxValue, hi = -lo;
            foreach (Vector3 p in points)
            {
                Vector3 q = inv * p;
                lo = lo.Min(q);
                hi = hi.Max(q);
            }
            Vector3 size = (hi - lo).Max(Vector3.One * MinThickness);
            float volume = size.X * size.Y * size.Z;
            if (volume < best - 1e-6f)
            {
                best = volume;
                fit = (b, b * ((lo + hi) / 2), size);
            }
        }
        return fit;
    }

    /// <summary>The axes along which the points spread most and least (Jacobi on their covariance).</summary>
    private static Basis Principal(Vector3[] points)
    {
        Vector3 mean = points.Aggregate(Vector3.Zero, (a, p) => a + p) / points.Length;
        var c = new double[3, 3];
        foreach (Vector3 p in points)
        {
            Vector3 d = p - mean;
            for (int i = 0; i < 3; i++)
                for (int j = 0; j < 3; j++)
                    c[i, j] += d[i] * d[j];
        }
        var v = new double[3, 3] { { 1, 0, 0 }, { 0, 1, 0 }, { 0, 0, 1 } };
        for (int sweep = 0; sweep < 50; sweep++)
            for (int p = 0; p < 2; p++)
                for (int q = p + 1; q < 3; q++)
                {
                    if (Math.Abs(c[p, q]) < 1e-12)
                        continue;
                    double theta = (c[q, q] - c[p, p]) / (2 * c[p, q]);
                    double t = Math.Sign(theta) / (Math.Abs(theta) + Math.Sqrt(theta * theta + 1));
                    if (theta == 0)
                        t = 1;
                    double cs = 1 / Math.Sqrt(t * t + 1), sn = t * cs;
                    for (int k = 0; k < 3; k++)
                    {
                        double ckp = c[k, p], ckq = c[k, q];
                        c[k, p] = cs * ckp - sn * ckq;
                        c[k, q] = sn * ckp + cs * ckq;
                    }
                    for (int k = 0; k < 3; k++)
                    {
                        double cpk = c[p, k], cqk = c[q, k];
                        c[p, k] = cs * cpk - sn * cqk;
                        c[q, k] = sn * cpk + cs * cqk;
                    }
                    for (int k = 0; k < 3; k++)
                    {
                        double vkp = v[k, p], vkq = v[k, q];
                        v[k, p] = cs * vkp - sn * vkq;
                        v[k, q] = sn * vkp + cs * vkq;
                    }
                }
        var x = new Vector3((float)v[0, 0], (float)v[1, 0], (float)v[2, 0]).Normalized();
        var y = new Vector3((float)v[0, 1], (float)v[1, 1], (float)v[2, 1]).Normalized();
        return new Basis(x, y, x.Cross(y));
    }
}
