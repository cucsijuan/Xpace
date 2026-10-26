using System;
using System.Collections.Generic;
using Godot;

namespace Xpace.Space;

/// <summary>
/// Something solid that does not move: a station's module, a turret on it.
/// Its surface is a set of triangles, taken from <see cref="Shape"/> (a
/// model whose meshes are the surface: Synty's collision meshes) and from
/// the box shapes below it (<see cref="CollisionShape3D"/> with a
/// <see cref="BoxShape3D"/>: for small parts with no collision mesh).
/// Ships' hitboxes meet it in <see cref="Ship.ShipContact"/>, and rounds,
/// missiles and drones stop against it (<see cref="Obstacles"/>).
///
/// The triangles are kept in a frame turned like the node but not scaled
/// (a scaled part has its scale in its triangles), on a grid of
/// <see cref="CellSize"/> cubes, so a ship near one looks only at the
/// triangles round it. Without its model (the Synty files not imported)
/// a solid has only its boxes, or nothing.
/// </summary>
[GlobalClass]
public partial class Solid : Node3D
{
    /// <summary>A model whose meshes are the surface (its nodes' transforms applied).</summary>
    [Export] public PackedScene Shape;
    /// <summary>The grid's cell (m): about the size of a ship.</summary>
    [Export] public float CellSize = 25f;

    public static readonly List<Solid> All = new();

    /// <summary>Its triangles, in its unscaled frame.</summary>
    public Vector3[] A { get; private set; } = Array.Empty<Vector3>();
    public Vector3[] B { get; private set; } = Array.Empty<Vector3>();
    public Vector3[] C { get; private set; } = Array.Empty<Vector3>();
    /// <summary>The box (its unscaled frame) holding every triangle.</summary>
    public Aabb Bounds { get; private set; }
    /// <summary>The radius about its origin that holds every triangle.</summary>
    public float Reach { get; private set; }

    private readonly Dictionary<long, List<int>> _cells = new();
    private int[] _seen = Array.Empty<int>();
    private int _stamp;
    private Basis _turn = Basis.Identity;           // the unscaled frame's turn
    private Basis _baked;                           // the global basis the triangles were made with

    public override void _EnterTree() => All.Add(this);
    public override void _ExitTree() => All.Remove(this);

    public override void _Ready() => Bake();

    /// <summary>The unscaled frame: its turn and its place in the scene.</summary>
    public Transform3D Frame
    {
        get
        {
            if (GlobalBasis != _baked)
                Bake();
            return new Transform3D(_turn, GlobalPosition);
        }
    }

    /// <summary>Makes the triangles again, from its model and boxes as it now stands.</summary>
    public void Bake()
    {
        Transform3D global = GlobalTransform;
        _baked = global.Basis;
        _turn = global.Basis.Orthonormalized();
        Transform3D toFrame = new Transform3D(_turn, global.Origin).AffineInverse();
        var a = new List<Vector3>();
        var b = new List<Vector3>();
        var c = new List<Vector3>();
        void Add(Vector3[] faces, Transform3D toGlobal)
        {
            for (int i = 0; i + 2 < faces.Length; i += 3)
            {
                a.Add(toFrame * (toGlobal * faces[i]));
                b.Add(toFrame * (toGlobal * faces[i + 1]));
                c.Add(toFrame * (toGlobal * faces[i + 2]));
            }
        }

        if (Shape != null)
        {
            Node model = Shape.Instantiate();
            Meshes(model, global, Add);
            model.Free();
        }
        foreach (Node n in FindChildren("*", nameof(CollisionShape3D), true, false))
            if (n is CollisionShape3D { Shape: BoxShape3D box } shape)
                Add(new BoxMesh { Size = box.Size }.GetFaces(), shape.GlobalTransform);

        A = a.ToArray();
        B = b.ToArray();
        C = c.ToArray();
        _seen = new int[A.Length];
        _cells.Clear();
        float reach = 0;
        var bounds = new Aabb(A.Length > 0 ? A[0] : Vector3.Zero, Vector3.Zero);
        for (int i = 0; i < A.Length; i++)
        {
            Aabb t = Box(i);
            bounds = bounds.Merge(t);
            reach = MathF.Max(reach, MathF.Max(A[i].Length(), MathF.Max(B[i].Length(), C[i].Length())));
            Cells(t.Position, t.End, key =>
            {
                if (!_cells.TryGetValue(key, out List<int> list))
                    _cells[key] = list = new List<int>();
                list.Add(i);
            });
        }
        Bounds = bounds;
        Reach = reach;
    }

    private static void Meshes(Node node, Transform3D parent, Action<Vector3[], Transform3D> add)
    {
        Transform3D at = node is Node3D n3 ? parent * n3.Transform : parent;
        if (node is MeshInstance3D { Mesh: not null } mesh)
            add(mesh.Mesh.GetFaces(), at);
        foreach (Node child in node.GetChildren())
            Meshes(child, at, add);
    }

    private Aabb Box(int i)
    {
        Vector3 lo = A[i].Min(B[i]).Min(C[i]);
        Vector3 hi = A[i].Max(B[i]).Max(C[i]);
        return new Aabb(lo, hi - lo);
    }

    private void Cells(Vector3 lo, Vector3 hi, Action<long> each)
    {
        int x0 = Cell(lo.X), y0 = Cell(lo.Y), z0 = Cell(lo.Z);
        int x1 = Cell(hi.X), y1 = Cell(hi.Y), z1 = Cell(hi.Z);
        for (int x = x0; x <= x1; x++)
            for (int y = y0; y <= y1; y++)
                for (int z = z0; z <= z1; z++)
                    each(Key(x, y, z));
    }

    private int Cell(float v) => (int)MathF.Floor(v / CellSize);
    private static long Key(int x, int y, int z) => ((long)(x & 0x1FFFFF) << 42) | ((long)(y & 0x1FFFFF) << 21) | (uint)(z & 0x1FFFFF);

    /// <summary>The triangles that may lie in a box of its unscaled frame (each once).</summary>
    public void Near(Vector3 lo, Vector3 hi, List<int> into)
    {
        into.Clear();
        if (A.Length == 0)
            return;
        lo = lo.Max(Bounds.Position);
        hi = hi.Min(Bounds.End);
        if (lo.X > hi.X || lo.Y > hi.Y || lo.Z > hi.Z)
            return;
        if (++_stamp == int.MaxValue)
        {
            Array.Clear(_seen);
            _stamp = 1;
        }
        Cells(lo, hi, key =>
        {
            if (!_cells.TryGetValue(key, out List<int> list))
                return;
            foreach (int i in list)
                if (_seen[i] != _stamp)
                {
                    _seen[i] = _stamp;
                    into.Add(i);
                }
        });
    }

    private readonly List<int> _near = new();

    /// <summary>
    /// Where a segment (scene points) first meets its surface: the share of
    /// the way along it and the surface's normal there (scene axes, facing
    /// back along the segment). For short segments: a step's worth.
    /// </summary>
    public bool Segment(Vector3 from, Vector3 to, out float along, out Vector3 normal)
    {
        along = 1;
        normal = Vector3.Zero;
        Transform3D frame = Frame;
        if (from.DistanceTo(frame.Origin) > Reach + from.DistanceTo(to))
            return false;
        Transform3D toFrame = frame.AffineInverse();
        Vector3 p = toFrame * from, q = toFrame * to;
        Near(p.Min(q), p.Max(q), _near);
        Vector3 d = q - p;
        bool hit = false;
        foreach (int i in _near)
        {
            // Möller and Trumbore
            Vector3 e1 = B[i] - A[i], e2 = C[i] - A[i];
            Vector3 s = d.Cross(e2);
            float det = e1.Dot(s);
            if (MathF.Abs(det) < 1e-12f)
                continue;
            float inv = 1 / det;
            Vector3 o = p - A[i];
            float u = o.Dot(s) * inv;
            if (u < 0 || u > 1)
                continue;
            Vector3 r = o.Cross(e1);
            float v = d.Dot(r) * inv;
            if (v < 0 || u + v > 1)
                continue;
            float t = e2.Dot(r) * inv;
            if (t < 0 || t >= along)
                continue;
            along = t;
            Vector3 n = e1.Cross(e2);
            normal = n.Dot(d) > 0 ? -n : n;
            hit = true;
        }
        if (hit)
            normal = (frame.Basis * normal).Normalized();
        return hit;
    }

    /// <summary>The nearest of every solid's <see cref="Segment"/>.</summary>
    public static bool SegmentAll(Vector3 from, Vector3 to, out float along, out Vector3 normal)
    {
        along = 1;
        normal = Vector3.Zero;
        bool hit = false;
        foreach (Solid s in All)
            if (s.Segment(from, to, out float t, out Vector3 n) && t < along)
            {
                along = t;
                normal = n;
                hit = true;
            }
        return hit;
    }
}
