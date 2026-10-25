using System;
using System.Collections.Generic;
using Godot;

namespace Xpace.Space;

/// <summary>A rock of a field, by name: its cell and its place in the cell. The same rock on every machine.</summary>
public readonly record struct RockId(int X, int Y, int Z, int Index);

/// <summary>A solid sphere of a rock, in scene coordinates.</summary>
public struct RockSphere
{
    public Vector3 Centre;
    public float Radius;
    public RockId Rock;
}

/// <summary>
/// A field of rocks: an asteroid field, or a planet's ring. Nothing is
/// placed by hand and nothing is kept for rocks far from everything: space
/// is cut into cells of <see cref="CellSize"/>, and each cell's rocks are
/// worked out from the field's <see cref="Seed"/> and the cell's place
/// whenever they are wanted, the same on every machine (integer hashing,
/// and only + - * / and square roots). How many there are follows the
/// <see cref="Region"/>'s density; what they are, the <see cref="Kinds"/>.
///
/// Drawing: cells are grouped into chunks of <see cref="ChunkCells"/>³,
/// and each chunk near the camera is one multimesh per kind of rock, made
/// straight on the rendering server (no nodes). Big rocks show out to
/// <see cref="DrawRange"/>, small ones to <see cref="SmallDrawRange"/>.
///
/// Touching: rocks are solid as the spheres of their kind
/// (<see cref="RockKind.Spheres"/>). <see cref="Overlap"/> gives the
/// spheres near a point (ships colliding), <see cref="Segment"/> the first
/// one along a segment (rounds, missiles, and the line of sight of radars
/// and heat seekers).
///
/// Radar clutter: rocks echo, but nobody shows them. <see cref="Clutter"/>
/// gives what the rocks sharing a target's resolution cell (beam, range,
/// Doppler) send back with it; a radar counts that with the noise. Rocks
/// stand still, so a ship moving along the line of sight is apart from
/// them in Doppler; one sitting still beside a rock is lost in its echo.
///
/// The field's frame is where the node is when the scene starts, kept in
/// double precision: <see cref="FloatingOrigin"/> shifts do not move it.
/// A rock taken away (<see cref="Remove"/>, mining) stays gone.
/// </summary>
public partial class RockField : Node3D, IOriginShift
{
    [Export] public RockRegion Region = new RockBox();
    [Export] public Godot.Collections.Array<RockKind> Kinds = new();
    [Export] public int Seed = 4770;
    /// <summary>Metres along a cell's side.</summary>
    [Export] public float CellSize = 500f;
    /// <summary>Rocks in a cell where the region's density is 1.</summary>
    [Export] public int RocksPerCell = 6;
    /// <summary>Rocks are their kind's size times a scale from this...</summary>
    [Export] public float MinScale = 1f;
    /// <summary>...to this.</summary>
    [Export] public float MaxScale = 12f;
    /// <summary>How strongly sizes lean to the small end: the scale's random share is raised to this power.</summary>
    [Export(PropertyHint.Range, "1,8")] public int SizeSkew = 3;
    /// <summary>Lightest and darkest a rock is shaded.</summary>
    [Export] public Vector2 Shade = new(0.7f, 1.1f);

    [ExportGroup("Drawing")]
    [Export] public float DrawRange = 12_000f;
    /// <summary>Rocks less than <see cref="SmallSize"/> across show only out to this.</summary>
    [Export] public float SmallDrawRange = 3_000f;
    [Export] public float SmallSize = 40f;
    [Export] public int ChunkCells = 4;
    /// <summary>Chunks made at most each frame (more would stutter).</summary>
    [Export] public int ChunksPerFrame = 6;
    [Export] public bool CastShadows = false;

    /// <summary>Every field in the scene, for queries that do not care which.</summary>
    public static readonly List<RockField> All = new();

    public struct Rock
    {
        public RockId Id;
        /// <summary>Field frame, from the cell's centre.</summary>
        public Vector3 Offset;
        public Quaternion Turn;
        public float Scale;
        public float Reach;
        public byte Kind;
        public float Shade;
    }

    private sealed class Cell
    {
        public Rock[] Rocks;
        public ulong Used;
    }

    private sealed class Chunk
    {
        public readonly List<Rid> Instances = new();
        public readonly List<Rid> Multimeshes = new();
        public Vector3D Centre;
    }

    private Vector3D _centre;                   // the field's origin in the sector
    private Basis _basis = Basis.Identity;      // field frame -> scene axes
    private Basis _inverse = Basis.Identity;
    private Vector3D _originLocal;              // the scene's origin, in the field's frame
    private FloatingOrigin _floating;
    private bool _placed;

    private RockKind[] _kinds = Array.Empty<RockKind>();
    private float[] _weights = Array.Empty<float>();
    private float _totalWeight;
    private Vector4[][] _spheres = Array.Empty<Vector4[]>();
    private float[] _reach = Array.Empty<float>();
    private float[] _echo = Array.Empty<float>();        // radar cross-section of the spheres as they are (a rock's is this times its scale²)
    private float _maxReach;

    private readonly Dictionary<(int, int, int), Cell> _cells = new();
    private readonly HashSet<RockId> _removed = new();
    private readonly Dictionary<(int, int, int), Chunk> _chunks = new();
    private readonly HashSet<(int, int, int)> _dirty = new();
    private readonly HashSet<(int, int, int)> _visit = new();
    private ulong _uses;
    private Vector3D _lastEye;
    private bool _haveEye;

    public int CachedCells => _cells.Count;
    public int ChunksShown => _chunks.Count;

    public override void _EnterTree() => All.Add(this);
    public override void _ExitTree()
    {
        All.Remove(this);
        foreach (Chunk c in _chunks.Values)
            Free(c);
        _chunks.Clear();
    }

    public override void _Ready() => Place();

    /// <summary>Takes the field's frame from where the node is now and loads the kinds (done at ready; again if the field is set up in code).</summary>
    public void Place()
    {
        _floating = null;
        for (Node n = GetParent(); n != null; n = n.GetParent())
            if (n is FloatingOrigin f)
            {
                _floating = f;
                break;
            }
        SectorPoint origin = _floating?.Origin ?? default;
        Vector3 at = GlobalPosition;
        _centre = new Vector3D(origin.X + at.X, origin.Y + at.Y, origin.Z + at.Z);
        _basis = GlobalBasis.Orthonormalized();
        _inverse = _basis.Transposed();

        var kinds = new List<RockKind>();
        foreach (RockKind k in Kinds)
            if (k != null && k.Weight > 0)
                kinds.Add(k);
        _kinds = kinds.ToArray();
        _weights = new float[_kinds.Length];
        _spheres = new Vector4[_kinds.Length][];
        _reach = new float[_kinds.Length];
        _echo = new float[_kinds.Length];
        _totalWeight = 0;
        _maxReach = 0;
        for (int i = 0; i < _kinds.Length; i++)
        {
            RockKind k = _kinds[i];
            _spheres[i] = k.Spheres.Length > 0 ? k.Spheres : new[] { new Vector4(0, 0, 0, 1) };
            float reach = 0;
            foreach (Vector4 s in _spheres[i])
                reach = MathF.Max(reach, MathF.Sqrt(s.X * s.X + s.Y * s.Y + s.Z * s.Z) + s.W);
            _reach[i] = reach * k.Scale;
            double volume = 0;
            foreach (Vector4 sp in _spheres[i])
                volume += (double)sp.W * sp.W * sp.W;
            float size = MathF.Min((float)Math.Cbrt(volume), reach);
            _echo[i] = k.Reflectivity * MathF.PI * size * size;
            _maxReach = MathF.Max(_maxReach, _reach[i] * MaxScale);
            _totalWeight += k.Weight;
            _weights[i] = _totalWeight;
        }
        _cells.Clear();
        foreach (Chunk c in _chunks.Values)
            Free(c);
        _chunks.Clear();
        _haveEye = false;
        _placed = true;
        UpdateOrigin();
    }

    public void ShiftOrigin(Vector3 offset)
    {
        UpdateOrigin();
        foreach (Chunk c in _chunks.Values)
            foreach (Rid i in c.Instances)
                RenderingServer.InstanceSetTransform(i, ChunkTransform(c.Centre));
    }

    private void UpdateOrigin()
    {
        SectorPoint o = _floating?.Origin ?? default;
        _originLocal = Rotate(_inverse, new Vector3D(o.X - _centre.X, o.Y - _centre.Y, o.Z - _centre.Z));
    }

    // ---------- frames ----------

    /// <summary>A scene point in the field's frame.</summary>
    public Vector3D FieldPoint(Vector3 scene) => _originLocal + Rotate(_inverse, scene);

    /// <summary>A point of the field's frame in the scene.</summary>
    public Vector3 ScenePoint(Vector3D local) => Rotate(_basis, local - _originLocal).ToVector3();

    private static Vector3D Rotate(Basis b, Vector3D v) => new(
        b.X.X * v.X + b.Y.X * v.Y + b.Z.X * v.Z,
        b.X.Y * v.X + b.Y.Y * v.Y + b.Z.Y * v.Z,
        b.X.Z * v.X + b.Y.Z * v.Y + b.Z.Z * v.Z);

    private Vector3D CellCentre(int x, int y, int z) => new((x + 0.5) * CellSize, (y + 0.5) * CellSize, (z + 0.5) * CellSize);

    private int CellOf(double v) => (int)Math.Floor(v / CellSize);

    // ---------- the rocks ----------

    /// <summary>A cell's rocks (worked out, or remembered from the last time).</summary>
    public Rock[] RocksIn(int x, int y, int z)
    {
        if (!_placed)
            Place();
        if (!_cells.TryGetValue((x, y, z), out Cell cell))
        {
            cell = new Cell { Rocks = Generate(x, y, z) };
            if (_cells.Count > 50_000)
                Forget();
            _cells[(x, y, z)] = cell;
        }
        cell.Used = ++_uses;
        return cell.Rocks;
    }

    private void Forget()
    {
        ulong keepAfter = _uses > 20_000 ? _uses - 20_000 : 0;
        var old = new List<(int, int, int)>();
        foreach (var (key, cell) in _cells)
            if (cell.Used < keepAfter)
                old.Add(key);
        foreach (var key in old)
            _cells.Remove(key);
    }

    private Rock[] Generate(int x, int y, int z)
    {
        if (_kinds.Length == 0 || Region == null)
            return Array.Empty<Rock>();
        Vector3D centre = CellCentre(x, y, z);
        var (min, max) = Region.Bounds;
        double h = CellSize / 2.0;
        for (int a = 0; a < 3; a++)
            if (centre[a] + h < min[a] || centre[a] - h > max[a])
                return Array.Empty<Rock>();

        var rng = new Rng(Hash((uint)Seed, x, y, z));
        var rocks = new List<Rock>();
        for (int i = 0; i < RocksPerCell; i++)
        {
            // Every candidate draws the same numbers, kept or not: a rock's
            // fate never changes the next one's
            var offset = new Vector3((rng.Unit() - 0.5f) * CellSize, (rng.Unit() - 0.5f) * CellSize, (rng.Unit() - 0.5f) * CellSize);
            float keep = rng.Unit();
            float pick = rng.Unit() * _totalWeight;
            float size = rng.Unit();
            Quaternion turn = rng.Turn();
            float shade = rng.Unit();
            var id = new RockId(x, y, z, i);
            if (keep >= Region.Density(centre.X + offset.X, centre.Y + offset.Y, centre.Z + offset.Z) || _removed.Contains(id))
                continue;
            int kind = 0;
            while (kind < _kinds.Length - 1 && pick >= _weights[kind])
                kind++;
            float skewed = size;
            for (int p = 1; p < SizeSkew; p++)
                skewed *= size;
            float scale = (MinScale + (MaxScale - MinScale) * skewed) * _kinds[kind].Scale;
            rocks.Add(new Rock
            {
                Id = id, Offset = offset, Turn = turn, Scale = scale,
                Reach = _reach[kind] / _kinds[kind].Scale * scale, Kind = (byte)kind,
                Shade = Shade.X + (Shade.Y - Shade.X) * shade,
            });
        }
        return rocks.ToArray();
    }

    /// <summary>Takes a rock away for good (mined, blown up). False if there was none by that name.</summary>
    public bool Remove(RockId id)
    {
        bool had = Array.Exists(RocksIn(id.X, id.Y, id.Z), r => r.Id == id);
        if (!had)
            return false;
        _removed.Add(id);
        _cells.Remove((id.X, id.Y, id.Z));
        _dirty.Add(ChunkOf(id.X, id.Y, id.Z));
        return true;
    }

    /// <summary>A rock's place in the scene.</summary>
    public Vector3 Where(in Rock rock) => ScenePoint(CellCentre(rock.Id.X, rock.Id.Y, rock.Id.Z) + (Vector3D)rock.Offset);

    // ---------- touching ----------

    /// <summary>
    /// Every solid sphere of every rock that comes within
    /// <paramref name="radius"/> of a scene point, added to <paramref name="into"/>.
    /// </summary>
    public void Overlap(Vector3 point, float radius, List<RockSphere> into)
    {
        if (!_placed)
            Place();
        Vector3D p = FieldPoint(point);
        double reach = radius + _maxReach;
        if (!NearBounds(p, reach))
            return;
        int x0 = CellOf(p.X - reach), x1 = CellOf(p.X + reach);
        int y0 = CellOf(p.Y - reach), y1 = CellOf(p.Y + reach);
        int z0 = CellOf(p.Z - reach), z1 = CellOf(p.Z + reach);
        for (int x = x0; x <= x1; x++)
            for (int y = y0; y <= y1; y++)
                for (int z = z0; z <= z1; z++)
                {
                    Vector3D cc = CellCentre(x, y, z);
                    foreach (Rock r in RocksIn(x, y, z))
                    {
                        // From the point to the rock: small, so a float holds it
                        Vector3 rel = (cc + (Vector3D)r.Offset - p).ToVector3();
                        float near = radius + r.Reach;
                        if (rel.LengthSquared() > near * near)
                            continue;
                        foreach (Vector4 s in _spheres[r.Kind])
                        {
                            Vector3 c = rel + r.Turn * (new Vector3(s.X, s.Y, s.Z) * r.Scale);
                            float sr = s.W * r.Scale;
                            if (c.LengthSquared() <= (radius + sr) * (radius + sr))
                                into.Add(new RockSphere { Centre = point + _basis * c, Radius = sr, Rock = r.Id });
                        }
                    }
                }
    }

    /// <summary>
    /// The first solid sphere a segment between two scene points runs
    /// into: how far along it (0 at <paramref name="from"/>, 1 at
    /// <paramref name="to"/>) and the surface's normal there.
    /// </summary>
    public bool Segment(Vector3 from, Vector3 to, out float along, out Vector3 normal, out RockId rock)
    {
        along = 1;
        normal = Vector3.Zero;
        rock = default;
        if (!_placed)
            Place();
        Vector3D a = FieldPoint(from);
        Vector3 d = Rotate(_inverse, (Vector3D)(to - from)).ToVector3();       // the segment, field axes
        if (!Clip(a, d, _maxReach, out float t0, out float t1))
            return false;

        // The cells the segment's tube passes, a cell at a time along it
        _visit.Clear();
        float length = d.Length();
        double margin = _maxReach + CellSize * 0.5;
        int steps = Math.Max(1, (int)Math.Ceiling(length * (t1 - t0) / CellSize));
        for (int i = 0; i <= steps; i++)
        {
            Vector3D q = a + (Vector3D)(d * (t0 + (t1 - t0) * i / steps));
            for (int x = CellOf(q.X - margin); x <= CellOf(q.X + margin); x++)
                for (int y = CellOf(q.Y - margin); y <= CellOf(q.Y + margin); y++)
                    for (int z = CellOf(q.Z - margin); z <= CellOf(q.Z + margin); z++)
                        _visit.Add((x, y, z));
        }

        bool hit = false;
        foreach (var (x, y, z) in _visit)
        {
            Vector3D cc = CellCentre(x, y, z);
            foreach (Rock r in RocksIn(x, y, z))
            {
                Vector3 rel = (cc + (Vector3D)r.Offset - a).ToVector3();      // from the segment's start to the rock
                if (Sphere(rel, r.Reach, d) is not float near || near >= along)
                    continue;
                foreach (Vector4 s in _spheres[r.Kind])
                {
                    Vector3 c = rel + r.Turn * (new Vector3(s.X, s.Y, s.Z) * r.Scale);
                    if (Sphere(c, s.W * r.Scale, d) is float t && t < along)
                    {
                        along = t;
                        normal = _basis * (d * t - c).Normalized();
                        rock = r.Id;
                        hit = true;
                    }
                }
            }
        }
        return hit;
    }

    /// <summary>Where along 0..1 the segment 0..d first enters a sphere at c (0 if it starts inside), or null.</summary>
    private static float? Sphere(Vector3 c, float radius, Vector3 d)
    {
        float dd = d.LengthSquared();
        float cc = c.LengthSquared() - radius * radius;
        if (cc <= 0)
            return 0;
        if (dd <= 0)
            return null;
        float b = c.Dot(d);
        if (b <= 0)
            return null;
        float disc = b * b - dd * cc;
        if (disc < 0)
            return null;
        float t = (b - MathF.Sqrt(disc)) / dd;
        return t <= 1 ? t : null;
    }

    private bool NearBounds(Vector3D p, double margin)
    {
        if (Region == null)
            return false;
        var (min, max) = Region.Bounds;
        for (int a = 0; a < 3; a++)
            if (p[a] < min[a] - margin || p[a] > max[a] + margin)
                return false;
        return true;
    }

    /// <summary>The part t0..t1 of the segment a + t d (t in 0..1) inside the region's bounds grown by a margin.</summary>
    private bool Clip(Vector3D a, Vector3 d, double margin, out float t0, out float t1)
    {
        t0 = 0;
        t1 = 1;
        if (Region == null)
            return false;
        var (min, max) = Region.Bounds;
        for (int axis = 0; axis < 3; axis++)
        {
            double lo = min[axis] - margin, hi = max[axis] + margin, start = a[axis], move = d[axis];
            if (Math.Abs(move) < 1e-9)
            {
                if (start < lo || start > hi)
                    return false;
                continue;
            }
            double ta = (lo - start) / move, tb = (hi - start) / move;
            if (ta > tb)
                (ta, tb) = (tb, ta);
            t0 = (float)Math.Max(t0, ta);
            t1 = (float)Math.Min(t1, tb);
            if (t0 > t1)
                return false;
        }
        return true;
    }

    /// <summary>
    /// The rocks' echo in a radar's resolution cell around a target, as a
    /// cross-section at the target's range (m²): each rock's echo, falling
    /// with range to the fourth, times how much of it is in the cell
    /// (half power half a cell out across the beam, in range and in
    /// Doppler, as the radar weighs echoes). A rock is as wide and as deep
    /// as its reach. The radar and the target move; the rocks do not.
    /// </summary>
    /// <param name="beam">The beam's width (radians).</param>
    /// <param name="rangeCell">The range resolution (m).</param>
    /// <param name="speedCell">The Doppler resolution (m/s).</param>
    public float Clutter(Vector3 radar, Vector3 radarVelocity, Vector3 target, Vector3 targetVelocity,
                         float beam, float rangeCell, float speedCell)
    {
        if (!_placed)
            Place();
        Vector3D p = FieldPoint(target);
        Vector3 q = (FieldPoint(radar) - p).ToVector3();             // the radar, from the target (field axes)
        float rt = q.Length();
        if (rt < 1)
            return 0;
        Vector3 ut = -q / rt;
        Vector3 vr = Rotate(_inverse, (Vector3D)radarVelocity).ToVector3();
        float targetDoppler = (Rotate(_inverse, (Vector3D)targetVelocity).ToVector3() - vr).Dot(ut);
        double reach = rt * beam * 1.5 + rangeCell * 1.5 + _maxReach;
        if (!NearBounds(p, reach))
            return 0;
        float sum = 0;
        int x0 = CellOf(p.X - reach), x1 = CellOf(p.X + reach);
        int y0 = CellOf(p.Y - reach), y1 = CellOf(p.Y + reach);
        int z0 = CellOf(p.Z - reach), z1 = CellOf(p.Z + reach);
        for (int x = x0; x <= x1; x++)
            for (int y = y0; y <= y1; y++)
                for (int z = z0; z <= z1; z++)
                {
                    Vector3D cc = CellCentre(x, y, z);
                    foreach (Rock r in RocksIn(x, y, z))
                    {
                        Vector3 rel = (cc + (Vector3D)r.Offset - p).ToVector3();
                        if (rel.LengthSquared() > reach * reach)
                            continue;
                        Vector3 toRock = rel - q;
                        float ri = toRock.Length();
                        if (ri < 1)
                            continue;
                        Vector3 ui = toRock / ri;
                        float across = MathF.Max(0, MathF.Acos(Math.Clamp(ui.Dot(ut), -1f, 1f)) - r.Reach / ri) / beam;
                        float deep = MathF.Max(0, MathF.Abs(ri - rt) - r.Reach) / rangeCell;
                        float doppler = (targetDoppler + vr.Dot(ui)) / speedCell;
                        float w = MathF.Exp(-2.77f * (across * across + deep * deep + doppler * doppler));
                        if (w < 1e-4f)
                            continue;
                        float fall = rt / ri;
                        fall *= fall;
                        sum += _echo[r.Kind] * r.Scale * r.Scale * w * fall * fall;
                    }
                }
        return sum;
    }

    // ---------- every field ----------

    /// <summary>The solid spheres of every field near a scene point.</summary>
    public static void OverlapAll(Vector3 point, float radius, List<RockSphere> into)
    {
        foreach (RockField f in All)
            f.Overlap(point, radius, into);
    }

    /// <summary>The first rock of any field a segment runs into.</summary>
    public static bool SegmentAll(Vector3 from, Vector3 to, out float along, out Vector3 normal)
    {
        along = 1;
        normal = Vector3.Zero;
        bool hit = false;
        foreach (RockField f in All)
            if (f.Segment(from, to, out float t, out Vector3 n, out _) && t < along)
            {
                along = t;
                normal = n;
                hit = true;
            }
        return hit;
    }

    /// <summary>Every field's rock echo in a radar's resolution cell around a target (see <see cref="Clutter"/>).</summary>
    public static float ClutterAll(Vector3 radar, Vector3 radarVelocity, Vector3 target, Vector3 targetVelocity,
                                   float beam, float rangeCell, float speedCell)
    {
        float sum = 0;
        foreach (RockField f in All)
            sum += f.Clutter(radar, radarVelocity, target, targetVelocity, beam, rangeCell, speedCell);
        return sum;
    }

    /// <summary>Whether a rock stands between two scene points (a radar's or a seeker's line of sight).</summary>
    public static bool Blocked(Vector3 from, Vector3 to)
    {
        foreach (RockField f in All)
            if (f.Segment(from, to, out _, out _, out _))
                return true;
        return false;
    }

    // ---------- drawing ----------

    private (int, int, int) ChunkOf(int x, int y, int z) =>
        (FloorDiv(x, ChunkCells), FloorDiv(y, ChunkCells), FloorDiv(z, ChunkCells));

    private static int FloorDiv(int a, int b) => a >= 0 ? a / b : -((-a + b - 1) / b);

    public override void _Process(double delta)
    {
        if (!_placed || _kinds.Length == 0 || GetViewport()?.GetCamera3D() is not Camera3D camera)
            return;
        Vector3D eye = FieldPoint(camera.GlobalPosition);
        double chunk = CellSize * ChunkCells;
        bool moved = !_haveEye || Math.Abs(eye.X - _lastEye.X) + Math.Abs(eye.Y - _lastEye.Y) + Math.Abs(eye.Z - _lastEye.Z) > chunk / 8;
        if (!moved && _dirty.Count == 0 && !_pending)
            return;
        _lastEye = eye;
        _haveEye = true;

        // Drop the chunks gone well out of range
        double keep = DrawRange + chunk * 1.5;
        var gone = new List<(int, int, int)>();
        foreach (var (key, c) in _chunks)
            if (Distance(c.Centre, eye) > keep)
                gone.Add(key);
        foreach (var key in gone)
        {
            Free(_chunks[key]);
            _chunks.Remove(key);
        }
        foreach (var key in _dirty)
            if (_chunks.Remove(key, out Chunk c))
                Free(c);
        _dirty.Clear();

        // Make the missing ones in range, nearest first, a few a frame
        double reach = DrawRange + chunk * 0.87;
        if (!NearBounds(eye, reach))
        {
            _pending = false;
            return;
        }
        var (min, max) = Region.Bounds;
        int Lo(int a) => (int)Math.Floor(Math.Max(eye[a] - reach, min[a] - _maxReach) / chunk);
        int Hi(int a) => (int)Math.Floor(Math.Min(eye[a] + reach, max[a] + _maxReach) / chunk);
        var wanted = new List<(double, (int, int, int))>();
        for (int x = Lo(0); x <= Hi(0); x++)
            for (int y = Lo(1); y <= Hi(1); y++)
                for (int z = Lo(2); z <= Hi(2); z++)
                {
                    if (_chunks.ContainsKey((x, y, z)))
                        continue;
                    double dist = Distance(new Vector3D((x + 0.5) * chunk, (y + 0.5) * chunk, (z + 0.5) * chunk), eye);
                    if (dist <= reach)
                        wanted.Add((dist, (x, y, z)));
                }
        wanted.Sort((p, q) => p.Item1.CompareTo(q.Item1));
        int made = 0;
        foreach (var (_, key) in wanted)
        {
            if (made++ >= ChunksPerFrame)
                break;
            _chunks[key] = Build(key);
        }
        _pending = wanted.Count > ChunksPerFrame;
    }

    private bool _pending;

    private static double Distance(Vector3D a, Vector3D b)
    {
        Vector3D d = a - b;
        return Math.Sqrt(d.X * d.X + d.Y * d.Y + d.Z * d.Z);
    }

    private Transform3D ChunkTransform(Vector3D centre) => new(_basis, ScenePoint(centre));

    /// <summary>A chunk's rocks as one multimesh for each kind and size band.</summary>
    private Chunk Build((int X, int Y, int Z) key)
    {
        double size = CellSize * ChunkCells;
        var chunk = new Chunk { Centre = new Vector3D((key.X + 0.5) * size, (key.Y + 0.5) * size, (key.Z + 0.5) * size) };
        int bands = _kinds.Length * 2;
        var buffers = new List<float>[bands];
        for (int cx = 0; cx < ChunkCells; cx++)
            for (int cy = 0; cy < ChunkCells; cy++)
                for (int cz = 0; cz < ChunkCells; cz++)
                {
                    int x = key.X * ChunkCells + cx, y = key.Y * ChunkCells + cy, z = key.Z * ChunkCells + cz;
                    Vector3 cell = (CellCentre(x, y, z) - chunk.Centre).ToVector3();
                    foreach (Rock r in RocksIn(x, y, z))
                    {
                        int band = r.Kind * 2 + (r.Reach * 2 < SmallSize ? 1 : 0);
                        List<float> buf = buffers[band] ??= new List<float>();
                        Basis b = new Basis(r.Turn).Scaled(Vector3.One * r.Scale);
                        Vector3 o = cell + r.Offset;
                        buf.AddRange(new[]
                        {
                            b.X.X, b.Y.X, b.Z.X, o.X,
                            b.X.Y, b.Y.Y, b.Z.Y, o.Y,
                            b.X.Z, b.Y.Z, b.Z.Z, o.Z,
                            r.Shade, r.Shade * 0.97f, r.Shade * 0.93f, 1,
                        });
                    }
                }
        Rid scenario = GetWorld3D().Scenario;
        Transform3D at = ChunkTransform(chunk.Centre);
        for (int band = 0; band < bands; band++)
        {
            if (buffers[band] is not List<float> buf)
                continue;
            RockKind kind = _kinds[band / 2];
            Rid mm = RenderingServer.MultimeshCreate();
            RenderingServer.MultimeshAllocateData(mm, buf.Count / 16, RenderingServer.MultimeshTransformFormat.Transform3D, true);
            RenderingServer.MultimeshSetMesh(mm, kind.Mesh.GetRid());
            RenderingServer.MultimeshSetBuffer(mm, buf.ToArray());
            Rid instance = RenderingServer.InstanceCreate2(mm, scenario);
            RenderingServer.InstanceSetTransform(instance, at);
            float range = band % 2 == 1 ? SmallDrawRange : DrawRange;
            RenderingServer.InstanceGeometrySetVisibilityRange(instance, 0, range, 0, range * 0.1f,
                RenderingServer.VisibilityRangeFadeMode.Self);
            RenderingServer.InstanceGeometrySetCastShadowsSetting(instance,
                CastShadows ? RenderingServer.ShadowCastingSetting.On : RenderingServer.ShadowCastingSetting.Off);
            chunk.Multimeshes.Add(mm);
            chunk.Instances.Add(instance);
        }
        return chunk;
    }

    private static void Free(Chunk c)
    {
        foreach (Rid i in c.Instances)
            RenderingServer.FreeRid(i);
        foreach (Rid m in c.Multimeshes)
            RenderingServer.FreeRid(m);
        c.Instances.Clear();
        c.Multimeshes.Clear();
    }

    // ---------- numbers that are the same everywhere ----------

    private static ulong Hash(uint seed, int x, int y, int z)
    {
        ulong h = seed * 0x9E3779B97F4A7C15UL;
        h = Mix(h ^ (uint)x);
        h = Mix(h ^ ((ulong)(uint)y << 21));
        h = Mix(h ^ ((ulong)(uint)z << 42));
        return h;
    }

    private static ulong Mix(ulong z)
    {
        z += 0x9E3779B97F4A7C15UL;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    /// <summary>SplitMix64: integers only, so every machine draws the same rocks.</summary>
    private struct Rng
    {
        private ulong _state;

        public Rng(ulong seed) => _state = seed;

        public ulong Next() => Mix(_state += 0x9E3779B97F4A7C15UL);

        /// <summary>0 (included) to 1 (not), 24 bits.</summary>
        public float Unit() => (Next() >> 40) * (1f / (1 << 24));

        /// <summary>A turn of any axis and angle, all equally likely: a point in the 4D ball, made unit length.</summary>
        public Quaternion Turn()
        {
            while (true)
            {
                float x = Unit() * 2 - 1, y = Unit() * 2 - 1, z = Unit() * 2 - 1, w = Unit() * 2 - 1;
                float n = x * x + y * y + z * z + w * w;
                if (n > 0.01f && n <= 1)
                {
                    float k = 1 / MathF.Sqrt(n);
                    return new Quaternion(x * k, y * k, z * k, w * k);
                }
            }
        }
    }
}
