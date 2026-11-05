using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Xpace.Ship;
using Xpace.Space;

namespace Xpace.Tests;

/// <summary>
/// What a big battle costs when every ship keeps its own computer: two sides
/// of real AI fighters (FighterAi, FLIGHT, FIRECTL and RWR running) fly at each
/// other, and the wall time of every physics step is measured. The emulated
/// computers alone are timed too (the same ticks again, with nothing else).
/// Not a pass/fail test beyond "the ships fought": it prints the numbers.
///
/// Run: XPACE_SWARM=15 Godot --headless --fixed-fps 60 --path . res://tests/swarm_test.tscn
/// XPACE_SWARM is the number of ships on each side (default 15), XPACE_SWARM_SECONDS how long they fight (default 40).
/// </summary>
public partial class SwarmTest : Node
{
    public override async void _Ready()
    {
        int side = int.TryParse(System.Environment.GetEnvironmentVariable("XPACE_SWARM"), out int n) ? n : 15;
        var world = new Node3D { Name = "World" };
        AddChild(world);
        world.AddChild(new Projectiles());
        var scene = GD.Load<PackedScene>("res://scenes/ships/ai_fighter.tscn");
        var ships = new List<ShipBody>();
        int columns = (int)Math.Ceiling(Math.Sqrt(side));
        for (int team = 0; team < 2; team++)
        {
            float x = team == 0 ? -2500 : 2500;
            for (int i = 0; i < side; i++)
            {
                var ship = scene.Instantiate<ShipBody>();
                ship.GetNode<Datalink>("Datalink").Channel = team == 0 ? "red" : "blue";
                world.AddChild(ship);
                var at = new Vector3(x, (i / columns - columns / 2) * 150, (i % columns - columns / 2) * 150);
                Vector3 toward = new(team == 0 ? 1 : -1, 0, 0);
                ship.GlobalTransform = new Transform3D(Basis.LookingAt(toward, Vector3.Up), at);
                ships.Add(ship);
            }
        }
        GD.Print($"     {side} against {side}: {ships.Count} ships");

        await Until(() => ships.All(s => s.Computer.ScreenText().Contains("Threat warning resident")), 120);

        GD.Print("     all computers up, the battle starts");
        var steps = new List<double>();
        long last = Stopwatch.GetTimestamp();
        int seconds = int.TryParse(System.Environment.GetEnvironmentVariable("XPACE_SWARM_SECONDS"), out int asked) ? asked : 40;
        for (int i = 0; i < seconds * 60; i++)
        {
            await Frames(1);
            long now = Stopwatch.GetTimestamp();
            steps.Add((now - last) * 1000.0 / Stopwatch.Frequency);
            last = now;
        }
        steps.Sort();
        double mean = steps.Average();
        GD.Print($"     whole physics step: mean {mean:0.00} ms, median {steps[steps.Count / 2]:0.00}, 95th {steps[(int)(steps.Count * 0.95)]:0.00}, worst {steps[^1]:0.00} ms (a frame at 60/s is 16.67 ms)");
        int wrecked = ships.Count(s => s.GetNode<ShipHull>("Hull").Wrecked);
        int losses = ships.Sum(s => s.GetNode<ShipHull>("Hull").Losses);
        int hunting = ships.Count(s => s.GetNode<FighterAi>("Pilot").Doing is "attacking" or "intercepting");
        GD.Print($"     after {seconds} s: {losses} ships lost, {wrecked} wrecked now, {hunting} of {ships.Count} attacking or intercepting");

        // The emulated computers alone: the next second of ticks, nothing else
        var watch = Stopwatch.StartNew();
        foreach (ShipBody s in ships)
            for (int t = 0; t < 60; t++)
                s.Computer.Advance(1.0 / 60);
        double perShipTick = watch.Elapsed.TotalMilliseconds / (ships.Count * 60);
        GD.Print($"     one computer, one tick: {perShipTick * 1000:0} us; all {ships.Count} computers, one tick: {perShipTick * ships.Count:0.00} ms ({perShipTick * ships.Count / mean * 100:0}% of the step)");

        // The same ticks with the computers shared among the processor's cores, a tick at a time as a step would
        watch.Restart();
        for (int t = 0; t < 60; t++)
            Parallel.ForEach(ships, s => s.Computer.Advance(1.0 / 60));
        double parallelTick = watch.Elapsed.TotalMilliseconds / 60;
        GD.Print($"     all {ships.Count} computers side by side on {System.Environment.ProcessorCount} cores, one tick: {parallelTick:0.00} ms");

        // The picture each computer redraws every frame, watched or not
        watch.Restart();
        foreach (ShipBody s in ships)
            for (int t = 0; t < 60; t++)
                s.Computer._Process(1.0 / 60);
        double drawTick = watch.Elapsed.TotalMilliseconds / 60;
        GD.Print($"     redrawing every screen, one frame: {drawTick:0.00} ms ({drawTick / mean * 100:0}% of the step)");

        // Each kind of device, and the pilot: the same calls the ship makes every step, timed one kind at a time
        var costs = new Dictionary<string, double>();
        const int rounds = 20;
        foreach (ShipBody s in ships)
        {
            var parts = s.FindChildren("*", "", true, false).OfType<IShipDevice>().ToList();
            foreach (IShipDevice d in parts)
            {
                string kind = d.GetType().Name;
                watch.Restart();
                for (int t = 0; t < rounds; t++)
                {
                    d.BeforeStep(s, 1.0 / 60);
                    d.AfterStep(s, 1.0 / 60);
                }
                costs[kind] = costs.GetValueOrDefault(kind) + watch.Elapsed.TotalMilliseconds / rounds;
            }
        }
        foreach (var (kind, ms) in costs.OrderByDescending(c => c.Value).Take(8))
            GD.Print($"     {kind}: {ms:0.00} ms a step for the {ships.Count} ships ({ms / ships.Count * 1000:0} us each)");

        // One lookup of a group in the ship's own sector, as every device does several times a step
        watch.Restart();
        int seen = 0;
        foreach (ShipBody s in ships)
            for (int t = 0; t < rounds; t++)
                seen += Sectors.InGroup(s, "radar_targets").Count();
        double lookup = watch.Elapsed.TotalMilliseconds / rounds;
        GD.Print($"     looking up the radar targets once from each ship: {lookup:0.00} ms a step ({lookup / ships.Count * 1000:0} us each, {seen / (ships.Count * rounds)} found)");

        bool fought = losses > 0 || hunting > 0;
        GD.Print(fought ? "ALL PASSED" : "FAIL nobody fought");
        System.GC.Collect();
        System.GC.WaitForPendingFinalizers();
        GetTree().Quit(fought ? 0 : 1);
    }

    private async Task Frames(int n)
    {
        for (int i = 0; i < n; i++)
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
    }

    private async Task<bool> Until(Func<bool> done, double seconds)
    {
        for (int i = 0; i < seconds * 60; i++)
        {
            if (done())
                return true;
            await Frames(1);
        }
        return done();
    }
}
