using System;
using System.Collections.Generic;
using Godot;

namespace Xpace.Space;

/// <summary>
/// A hashed grid of things at points, to ask who is near without looking at everyone: radios, radar echoes, heat
/// sources, anything that has a place. It is made for a moment (a physics step) and thrown away, so nothing in it
/// follows the things as they move; positions are where they stood when it was made.
///
/// A query visits only the cells it reaches. When it would visit more cells than there are things (an empty
/// neighbourhood, a huge radius), it looks at the things instead, so no question costs more than looking at all.
/// </summary>
public sealed class SpatialGrid<T> where T : class
{
    private readonly float _cell;
    private readonly Dictionary<long, List<int>> _cells = new();
    private readonly List<T> _items = new();
    private readonly List<Vector3> _at = new();
    private readonly List<float> _weight = new();
    private float _heaviest;

    /// <summary>Cells are cubes of this side, metres: about the distance things are asked about.</summary>
    public SpatialGrid(float cell) => _cell = Math.Max(cell, 1f);

    public int Count => _items.Count;

    /// <summary>
    /// Puts a thing at a point. Its weight says how far it reaches (see <see cref="Reaching"/>): a loud emitter, a large
    /// echo; leave it at 1 for things that all matter alike.
    /// </summary>
    public void Add(T item, Vector3 position, float weight = 1f)
    {
        int index = _items.Count;
        _items.Add(item);
        _at.Add(position);
        _weight.Add(weight);
        _heaviest = Math.Max(_heaviest, weight);
        long key = Key(Cell(position.X), Cell(position.Y), Cell(position.Z));
        if (!_cells.TryGetValue(key, out List<int> cell))
            _cells[key] = cell = new List<int>(4);
        cell.Add(index);
    }

    /// <summary>The things within a distance of a point (as the grid has them), in no order.</summary>
    public void Within(Vector3 centre, float radius, List<T> into)
    {
        long span = (long)(2 * Math.Ceiling(radius / _cell) + 1);
        if (span * span * span > _items.Count)
        {
            for (int i = 0; i < _items.Count; i++)
                if (_at[i].DistanceTo(centre) <= radius)
                    into.Add(_items[i]);
            return;
        }
        int x0 = Cell(centre.X - radius), x1 = Cell(centre.X + radius);
        int y0 = Cell(centre.Y - radius), y1 = Cell(centre.Y + radius);
        int z0 = Cell(centre.Z - radius), z1 = Cell(centre.Z + radius);
        for (int x = x0; x <= x1; x++)
            for (int y = y0; y <= y1; y++)
                for (int z = z0; z <= z1; z++)
                    if (_cells.TryGetValue(Key(x, y, z), out List<int> cell))
                        foreach (int i in cell)
                            if (_at[i].DistanceTo(centre) <= radius)
                                into.Add(_items[i]);
    }

    /// <summary>
    /// The things that reach a point: each one's distance to it is at most <paramref name="scale"/> times its own weight
    /// (a radar's range times the fourth root of an echo's size, a receiver's reach times the root of an emitter's power).
    /// Nearest-first order is not promised; the cells within the heaviest thing's reach are visited, or the things
    /// themselves when that is fewer.
    /// </summary>
    public void Reaching(Vector3 centre, float scale, List<T> into)
    {
        float radius = scale * _heaviest;
        long span = (long)(2 * Math.Ceiling(radius / _cell) + 1);
        if (span * span * span > _items.Count)
        {
            for (int i = 0; i < _items.Count; i++)
                if (_at[i].DistanceTo(centre) <= scale * _weight[i])
                    into.Add(_items[i]);
            return;
        }
        int x0 = Cell(centre.X - radius), x1 = Cell(centre.X + radius);
        int y0 = Cell(centre.Y - radius), y1 = Cell(centre.Y + radius);
        int z0 = Cell(centre.Z - radius), z1 = Cell(centre.Z + radius);
        for (int x = x0; x <= x1; x++)
            for (int y = y0; y <= y1; y++)
                for (int z = z0; z <= z1; z++)
                    if (_cells.TryGetValue(Key(x, y, z), out List<int> cell))
                        foreach (int i in cell)
                            if (_at[i].DistanceTo(centre) <= scale * _weight[i])
                                into.Add(_items[i]);
    }

    /// <summary>
    /// The nearest things that <paramref name="accept"/> takes, up to <paramref name="count"/> and within
    /// <paramref name="maxRange"/>, nearest first. It widens shell by shell round the point and stops when the next shell
    /// cannot hold anything nearer than the ones it has.
    /// </summary>
    public void Nearest(Vector3 centre, int count, float maxRange, Func<T, bool> accept, List<T> into)
    {
        var found = new List<(float Distance, int Index)>();
        int cx = Cell(centre.X), cy = Cell(centre.Y), cz = Cell(centre.Z);
        for (int r = 0; ; r++)
        {
            long reached = (long)(2 * r + 1);
            if (reached * reached * reached > _items.Count)
            {
                Everything(centre, maxRange, accept, found);
                break;
            }
            Shell(cx, cy, cz, r, centre, maxRange, accept, found);
            float guaranteed = r * _cell;                       // nothing outside the shells so far is nearer than this
            if (guaranteed >= maxRange)
                break;
            if (found.Count >= count)
            {
                found.Sort((a, b) => a.Distance.CompareTo(b.Distance));
                if (found[count - 1].Distance <= guaranteed)
                    break;
            }
        }
        found.Sort((a, b) => a.Distance.CompareTo(b.Distance));
        for (int i = 0; i < found.Count && i < count; i++)
            into.Add(_items[found[i].Index]);
    }

    private void Shell(int cx, int cy, int cz, int r, Vector3 centre, float maxRange, Func<T, bool> accept, List<(float, int)> found)
    {
        for (int dx = -r; dx <= r; dx++)
            for (int dy = -r; dy <= r; dy++)
            {
                bool wall = Math.Abs(dx) == r || Math.Abs(dy) == r;
                for (int dz = -r; dz <= r; dz += wall || r == 0 ? 1 : 2 * r)
                    if (_cells.TryGetValue(Key(cx + dx, cy + dy, cz + dz), out List<int> cell))
                        foreach (int i in cell)
                            Take(i, centre, maxRange, accept, found);
            }
    }

    private void Everything(Vector3 centre, float maxRange, Func<T, bool> accept, List<(float, int)> found)
    {
        found.Clear();
        for (int i = 0; i < _items.Count; i++)
            Take(i, centre, maxRange, accept, found);
    }

    private void Take(int i, Vector3 centre, float maxRange, Func<T, bool> accept, List<(float, int)> found)
    {
        float d = _at[i].DistanceTo(centre);
        if (d <= maxRange && accept(_items[i]))
            found.Add((d, i));
    }

    private int Cell(float metres) => (int)Math.Clamp(Math.Floor(metres / _cell), -1_000_000, 1_000_000);

    private static long Key(int x, int y, int z) => ((long)(x & 0x1FFFFF) << 42) | ((long)(y & 0x1FFFFF) << 21) | (long)(z & 0x1FFFFF);
}
