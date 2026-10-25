using System;
using System.Collections.Generic;
using Godot;
using Xpace.Space;

namespace Xpace.Tools;

/// <summary>
/// Fits the solid shape of each rock kind: a few spheres inside its model
/// that fill most of it. The model is cut into a grid of points, the points
/// inside it are found (a ray from each crosses its surface an odd number
/// of times), and each gets its distance to the surface: the biggest sphere
/// centred there that stays inside. Spheres are then picked one at a time,
/// each the one that covers most of what is not covered yet, until
/// <see cref="Coverage"/> of the inside is or the kind's
/// <see cref="RockKind.MaxSpheres"/> are used. The spheres go into the
/// kind's resource.
///
///   godot --headless --path . res://tools/rocks/rock_shape_tool.tscn [-- KIND.tres ...]
///
/// With no kinds named, every kind in <see cref="Folder"/>. See docs/tools/rocks.md.
/// </summary>
public partial class RockShapeTool : Node
{
    [Export(PropertyHint.Dir)] public string Folder = "res://assets/space/rocks";
    /// <summary>Grid points along the model's longest side.</summary>
    [Export] public int Resolution = 28;
    /// <summary>The share of the inside the spheres must cover before the tool stops adding them.</summary>
    [Export] public float Coverage = 0.95f;
    /// <summary>Spheres are grown by this many grid steps past the inside points' depth (the points lie half a step in from the surface at best).</summary>
    [Export] public float Grow = 0.5f;

    public override void _Ready()
    {
        var paths = new List<string>(OS.GetCmdlineUserArgs());
        if (paths.Count == 0)
            foreach (string f in DirAccess.GetFilesAt(Folder))
                if (f.EndsWith(".tres"))
                    paths.Add($"{Folder}/{f}");
        bool ok = paths.Count > 0;
        foreach (string path in paths)
            ok &= Fit(path);
        GetTree().Quit(ok ? 0 : 1);
    }

    private bool Fit(string path)
    {
        if (GD.Load(path) is not RockKind kind)
        {
            GD.PrintErr($"RockShapeTool: {path} is not a RockKind");
            return false;
        }
        Mesh mesh = RockKind.FindMesh(kind.Model);
        if (mesh == null)
        {
            GD.PrintErr($"RockShapeTool: no mesh in {kind.Model} (import the Synty art first)");
            return false;
        }
        var tris = new List<Vector3>();
        for (int s = 0; s < mesh.GetSurfaceCount(); s++)
        {
            var arrays = mesh.SurfaceGetArrays(s);
            Vector3[] v = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
            int[] idx = arrays[(int)Mesh.ArrayType.Index].AsInt32Array();
            if (idx.Length == 0)
                tris.AddRange(v);
            else
                foreach (int i in idx)
                    tris.Add(v[i]);
        }
        Vector4[] spheres = SphereFit.Fit(tris.ToArray(), mesh.GetAabb(), Resolution, Coverage, Grow, kind.MaxSpheres, out float covered);
        kind.Spheres = spheres;
        Error saved = ResourceSaver.Save(kind, path);
        // How far the model's surface stands out of the spheres (what a ship can sink into it)
        float worst = 0, sum = 0;
        foreach (Vector3 p in tris)
        {
            float gap = float.MaxValue;
            foreach (Vector4 s in spheres)
                gap = MathF.Min(gap, p.DistanceTo(new Vector3(s.X, s.Y, s.Z)) - s.W);
            gap = MathF.Max(gap, 0);
            worst = MathF.Max(worst, gap);
            sum += gap;
        }
        GD.Print($"{path}: {spheres.Length} spheres cover {covered:P0} of the inside, reach {kind.Reach:0.00}; " +
                 $"surface out of them by {sum / tris.Count:0.00} on average, {worst:0.00} at most " +
                 $"(model {mesh.GetAabb().Size.Length() / 2:0.00}){(saved == Error.Ok ? "" : $", NOT SAVED ({saved})")}");
        return saved == Error.Ok;
    }
}

/// <summary>Spheres inside a closed mesh that fill most of it (see <see cref="RockShapeTool"/>).</summary>
public static class SphereFit
{
    /// <param name="tris">Triangles, three corners each.</param>
    /// <returns>Spheres as (centre, radius).</returns>
    public static Vector4[] Fit(Vector3[] tris, Aabb box, int resolution, float coverage, float grow, int maxSpheres, out float covered)
    {
        float step = box.Size[(int)box.GetLongestAxisIndex()] / resolution;
        var inside = new List<Vector3>();
        var depth = new List<float>();
        for (float x = box.Position.X + step / 2; x < box.End.X; x += step)
            for (float y = box.Position.Y + step / 2; y < box.End.Y; y += step)
                for (float z = box.Position.Z + step / 2; z < box.End.Z; z += step)
                {
                    var p = new Vector3(x, y, z);
                    if (!Inside(p, tris))
                        continue;
                    inside.Add(p);
                    depth.Add(Distance(p, tris) + grow * step);
                }

        var spheres = new List<Vector4>();
        var done = new bool[inside.Count];
        int left = inside.Count;
        while (left > (1 - coverage) * inside.Count && spheres.Count < maxSpheres)
        {
            int best = -1, bestCount = 0;
            for (int c = 0; c < inside.Count; c++)
            {
                float r2 = depth[c] * depth[c];
                int count = 0;
                for (int i = 0; i < inside.Count; i++)
                    if (!done[i] && inside[i].DistanceSquaredTo(inside[c]) <= r2)
                        count++;
                if (count > bestCount)
                {
                    bestCount = count;
                    best = c;
                }
            }
            if (best < 0)
                break;
            Vector3 centre = inside[best];
            float radius = depth[best];
            spheres.Add(new Vector4(centre.X, centre.Y, centre.Z, radius));
            for (int i = 0; i < inside.Count; i++)
                if (!done[i] && inside[i].DistanceSquaredTo(centre) <= radius * radius)
                {
                    done[i] = true;
                    left--;
                }
        }
        covered = inside.Count == 0 ? 0 : 1 - (float)left / inside.Count;
        return spheres.ToArray();
    }

    /// <summary>Inside if rays along two of three axes cross the surface an odd number of times (a stray seam spoils one ray, not two).</summary>
    private static bool Inside(Vector3 p, Vector3[] tris)
    {
        int votes = 0;
        foreach (Vector3 dir in new[] { new Vector3(1, 0.0013f, 0.0007f), new Vector3(0.0011f, 1, 0.0005f), new Vector3(0.0009f, 0.0003f, 1) })
        {
            int crossings = 0;
            for (int t = 0; t < tris.Length; t += 3)
                if (Ray(p, dir, tris[t], tris[t + 1], tris[t + 2]))
                    crossings++;
            votes += crossings & 1;
        }
        return votes >= 2;
    }

    // Möller-Trumbore, forward only
    private static bool Ray(Vector3 o, Vector3 d, Vector3 a, Vector3 b, Vector3 c)
    {
        Vector3 e1 = b - a, e2 = c - a, h = d.Cross(e2);
        float det = e1.Dot(h);
        if (MathF.Abs(det) < 1e-12f)
            return false;
        float f = 1 / det;
        Vector3 s = o - a;
        float u = f * s.Dot(h);
        if (u < 0 || u > 1)
            return false;
        Vector3 q = s.Cross(e1);
        float v = f * d.Dot(q);
        if (v < 0 || u + v > 1)
            return false;
        return f * e2.Dot(q) > 0;
    }

    private static float Distance(Vector3 p, Vector3[] tris)
    {
        float best = float.MaxValue;
        for (int t = 0; t < tris.Length; t += 3)
            best = MathF.Min(best, p.DistanceSquaredTo(Closest(p, tris[t], tris[t + 1], tris[t + 2])));
        return MathF.Sqrt(best);
    }

    // Closest point on a triangle (Ericson, Real-Time Collision Detection 5.1.5)
    private static Vector3 Closest(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
    {
        Vector3 ab = b - a, ac = c - a, ap = p - a;
        float d1 = ab.Dot(ap), d2 = ac.Dot(ap);
        if (d1 <= 0 && d2 <= 0) return a;
        Vector3 bp = p - b;
        float d3 = ab.Dot(bp), d4 = ac.Dot(bp);
        if (d3 >= 0 && d4 <= d3) return b;
        float vc = d1 * d4 - d3 * d2;
        if (vc <= 0 && d1 >= 0 && d3 <= 0) return a + ab * (d1 / (d1 - d3));
        Vector3 cp = p - c;
        float d5 = ab.Dot(cp), d6 = ac.Dot(cp);
        if (d6 >= 0 && d5 <= d6) return c;
        float vb = d5 * d2 - d1 * d6;
        if (vb <= 0 && d2 >= 0 && d6 <= 0) return a + ac * (d2 / (d2 - d6));
        float va = d3 * d6 - d5 * d4;
        if (va <= 0 && d4 - d3 >= 0 && d5 - d6 >= 0) return b + (c - b) * ((d4 - d3) / (d4 - d3 + (d5 - d6)));
        float denom = 1 / (va + vb + vc);
        return a + ab * (vb * denom) + ac * (vc * denom);
    }
}
