using System;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Xpace.Ship;
using Xpace.Space;

namespace Xpace.Tests;

/// <summary>
/// Ships crossing a thick rock field, counting the crashes: drones with no
/// eyes for rocks, with a poor rock sensor (short, few rays) and with a
/// good one, down the same lanes through the same rocks; then the raider,
/// flown by its pilot through the computer, with no sensor and with one.
/// A better sensor must make a measurable difference.
/// Run: Godot --headless --fixed-fps 60 --path . res://tests/avoid_test.tscn
/// Exits with 0 if every check passed.
/// </summary>
public partial class AvoidTest : Node
{
    private const int Lanes = 16;
    private const float Start = 2500, End = -4500;      // z: before and beyond the field
    private int _failures;

    public override async void _Ready()
    {
        var origin = new FloatingOrigin { Name = "World" };
        AddChild(origin);
        var kinds = new Godot.Collections.Array<RockKind>(new[]
            { "rock_01", "rock_02", "rock_03", "rock_04", "rock_05", "rock_holes_03", "rock_large_01", "rock_large_02", "rock_large_holes_02" }
            .Select(k => GD.Load<RockKind>($"res://assets/space/rocks/{k}.tres")));
        // A slab 3 km deep across the lanes
        origin.AddChild(new RockField
        {
            Name = "Rocks", Kinds = kinds, Seed = 31, CellSize = 300, RocksPerCell = 12,
            Region = new RockBox { Size = new Vector3(9000, 1200, 3000), Falloff = 0 },
        });
        await Frames(1);

        // ---------- drones ----------
        int blind = await Drones(origin, null);
        int poor = await Drones(origin, () => new RockSensor { Range = 300, Rings = 1, RaysPerRing = 4, ScanSeconds = 0.5 });
        int good = await Drones(origin, () => new RockSensor());
        GD.Print($"drones crashed crossing the field: blind {blind}, poor sensor {poor}, good sensor {good} of {Lanes}");
        Check($"blind drones fly into rocks ({blind} of {Lanes})", blind >= 4);
        Check($"a poor sensor saves some ({poor})", poor < blind);
        Check($"a good one saves nearly all ({good})", good <= Math.Max(1, poor / 3));

        // ---------- the raider ----------
        var (blindRaider, blindCrash, _, _, _) = await Raider(origin, false);
        var (eyedRaider, eyedCrash, avoided, crossed, seconds) = await Raider(origin, true);
        GD.Print($"raider: blind {blindCrash} crashes ({blindRaider:0} hull), with a sensor {eyedCrash} ({eyedRaider:0} hull), " +
                 $"steered round rocks {avoided} times, crossed {crossed} of 3 in {seconds:0} s on average");
        Check($"the raider's pilot steers round rocks it sees ({avoided} times)", avoided > 0);
        Check($"and crashes less with eyes ({eyedCrash} against {blindCrash})", eyedCrash < blindCrash);
        Check($"and gets across, slowly where the rocks are thick ({crossed} of 3)", crossed == 3);

        GD.Print(_failures == 0 ? "ALL PASSED" : $"{_failures} FAILED");
        GetTree().Quit(_failures == 0 ? 0 : 1);
    }

    /// <summary>One drone down each lane, all at once: how many hit a rock.</summary>
    private async Task<int> Drones(Node origin, Func<RockSensor> sensor)
    {
        var drones = new Drone[Lanes];
        for (int i = 0; i < Lanes; i++)
        {
            float x = (i - (Lanes - 1) / 2f) * 450;
            var anchor = new Node3D { Name = $"Anchor{i}", Position = new Vector3(x, 0, End) };
            origin.AddChild(anchor);
            var drone = new Drone
            {
                Name = $"Drone{i}", Evades = false, Defends = false, WanderRadius = 150, RespawnSeconds = 1e6f,
                AnchorPath = anchor.GetPath(), Position = new Vector3(x, 0, Start),         // facing -Z: at the anchor
            };
            if (sensor != null)
                drone.AddChild(sensor());
            origin.AddChild(drone);
            drones[i] = drone;
        }
        int frames = (int)((Start - End) / 120f * 60) + 120;
        for (int f = 0; f < frames; f++)
            await Frames(1);
        int crashed = drones.Count(d => !d.Alive);
        foreach (Node n in origin.GetChildren())
            if (n is Drone || n.Name.ToString().StartsWith("Anchor"))
                n.QueueFree();
        await Frames(1);
        return crashed;
    }

    /// <summary>The raider down three lanes in turn: the hull it lost and how many crashes.</summary>
    private async Task<(float Hull, int Crashes, int Avoided, int Crossed, double Seconds)> Raider(Node origin, bool eyes)
    {
        float lost = 0;
        int crashes = 0, avoided = 0, crossed = 0;
        double seconds = 0;
        foreach (float x in new[] { -1500f, 200f, 1700f })
        {
            var raider = GD.Load<PackedScene>("res://scenes/ships/ai_fighter.tscn").Instantiate<ShipBody>();
            raider.Name = "Raider";
            raider.Position = new Vector3(x, 0, Start + 600);
            if (!eyes)
                raider.GetNode("RockSensor").Free();
            origin.AddChild(raider);
            var pilot = raider.GetNode<FighterAi>("Pilot");
            var hull = raider.GetNode<ShipHull>("Hull");
            hull.Insured = false;
            pilot.PatrolRadius = 100;
            pilot.Anchor = new Vector3(x, 0, End);
            float before = 0;
            int quiet = 60;
            bool wasAvoiding = false;
            int frames = 0;
            for (int f = 0; f < 300 * 60 && raider.GlobalPosition.Z > End + 300 && !raider.Wrecked; f++)
            {
                frames = f;
                await Frames(1);
                // A crash: the hull taking crash damage again after a second without
                if (hull.CrashDamage > before)
                {
                    if (quiet >= 60)
                        crashes++;
                    quiet = 0;
                }
                else
                    quiet++;
                before = hull.CrashDamage;
                bool avoiding = pilot.Doing == "avoiding a rock";
                if (avoiding && !wasAvoiding)
                    avoided++;
                wasAvoiding = avoiding;
            }
            lost += hull.CrashDamage;
            if (!raider.Wrecked && raider.GlobalPosition.Z <= End + 300)
            {
                crossed++;
                seconds += frames / 60.0;
            }
            raider.QueueFree();
            await Frames(1);
        }
        return (lost, crashes, avoided, crossed, crossed > 0 ? seconds / crossed : 0);
    }

    private async Task Frames(int n)
    {
        for (int i = 0; i < n; i++)
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
    }

    private void Check(string what, bool ok)
    {
        GD.Print((ok ? "ok   " : "FAIL ") + what);
        if (!ok)
            _failures++;
    }
}
