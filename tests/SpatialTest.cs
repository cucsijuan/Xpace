using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Godot;
using Xpace.Space;

namespace Xpace.Tests;

/// <summary>
/// The spatial grid answers what looking at everyone would: the things within a distance, and the nearest few that a rule
/// accepts, across empty space, crowds, negative coordinates and distances far past the cell. Then the cost: a question
/// in a crowd looks at the few near, not at all.
///
/// Run: Godot --headless --path . res://tests/spatial_test.tscn
/// Exits with 0 if every check passed.
/// </summary>
public partial class SpatialTest : Node
{
    private int _failures;

    public override void _Ready()
    {
        var rng = new Random(7);
        foreach (var (name, things, spread, cell) in new[]
                 {
                     ("a sparse sky", 60, 200_000f, 2000f),
                     ("a crowd", 400, 3_000f, 500f),
                     ("a few things", 5, 50_000f, 2000f),
                     ("a line of things", 200, 80_000f, 1000f),
                 })
        {
            var grid = new SpatialGrid<object>(cell);
            var at = new List<(object Item, Vector3 Where)>();
            for (int i = 0; i < things; i++)
            {
                Vector3 p = name == "a line of things"
                    ? new Vector3((float)(rng.NextDouble() - 0.5) * spread, 0, 0)
                    : new Vector3((float)(rng.NextDouble() - 0.5), (float)(rng.NextDouble() - 0.5), (float)(rng.NextDouble() - 0.5)) * spread;
                var item = new object();
                grid.Add(item, p);
                at.Add((item, p));
            }
            bool within = true, nearest = true, filtered = true;
            for (int q = 0; q < 200; q++)
            {
                Vector3 centre = new Vector3((float)(rng.NextDouble() - 0.5), (float)(rng.NextDouble() - 0.5), (float)(rng.NextDouble() - 0.5)) * spread * 1.2f;
                float radius = (float)rng.NextDouble() * spread * 0.4f;
                var got = new List<object>();
                grid.Within(centre, radius, got);
                var want = at.Where(a => a.Where.DistanceTo(centre) <= radius).Select(a => a.Item).ToHashSet();
                within &= want.SetEquals(got) && got.Count == want.Count;

                int count = 1 + rng.Next(16);
                var near = new List<object>();
                grid.Nearest(centre, count, float.MaxValue, _ => true, near);
                var expect = at.OrderBy(a => a.Where.DistanceTo(centre)).Take(count).Select(a => a.Item).ToList();
                // Things at the same distance (to the float) may come either way round: compare distances
                nearest &= near.Count == expect.Count &&
                           near.Zip(expect, (x, y) => MathF.Abs(Where(at, x).DistanceTo(centre) - Where(at, y).DistanceTo(centre)) < 1e-3f).All(ok => ok);

                var odd = new HashSet<object>(at.Where((_, i) => i % 3 == 0).Select(a => a.Item));
                var some = new List<object>();
                grid.Nearest(centre, count, radius, odd.Contains, some);
                var wanted = at.Where(a => odd.Contains(a.Item) && a.Where.DistanceTo(centre) <= radius)
                               .OrderBy(a => a.Where.DistanceTo(centre)).Take(count).Select(a => a.Item).ToList();
                filtered &= some.Count == wanted.Count &&
                            some.Zip(wanted, (x, y) => MathF.Abs(Where(at, x).DistanceTo(centre) - Where(at, y).DistanceTo(centre)) < 1e-3f).All(ok => ok);
            }
            Check($"{name}: everything within a distance is found, nothing else", within);
            Check($"{name}: the nearest few come back nearest first", nearest);
            Check($"{name}: with a rule and a range, the same as looking at all", filtered);
        }

        // The cost of one question in a crowd of 4,000 spread over 40 km: it must not look at them all
        var big = new SpatialGrid<object>(500);
        for (int i = 0; i < 4000; i++)
            big.Add(new object(), new Vector3((float)rng.NextDouble(), (float)rng.NextDouble(), (float)rng.NextDouble()) * 40_000f);
        int looked = 0;
        var result = new List<object>();
        var watch = Stopwatch.StartNew();
        for (int q = 0; q < 2000; q++)
            big.Nearest(new Vector3(20_000, 20_000, 20_000), 16, 6_000, _ => { looked++; return true; }, result);
        double perAsk = watch.Elapsed.TotalMilliseconds * 1000 / 2000;
        Check($"one question in 4,000 things looks at {looked / 2000} of them ({perAsk:0} us)", looked / 2000 < 400);

        GD.Print(_failures == 0 ? "ALL PASSED" : $"{_failures} FAILED");
        GetTree().Quit(_failures == 0 ? 0 : 1);
    }

    private static Vector3 Where(List<(object Item, Vector3 Where)> at, object item) => at.First(a => a.Item == item).Where;

    private void Check(string what, bool ok)
    {
        GD.Print((ok ? "ok   " : "FAIL ") + what);
        if (!ok)
            _failures++;
    }
}
