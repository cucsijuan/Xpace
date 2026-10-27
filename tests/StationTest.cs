using System.Linq;
using System.Threading.Tasks;
using Godot;
using Xpace.Computer;
using Xpace.Ship;
using Xpace.Space;

namespace Xpace.Tests;

/// <summary>
/// The station from Synty's demo, built of modules: every module solid
/// with its collision mesh; segments stopping on its walls; ships flying
/// into it, slow and fast, head on and from above, bouncing off and never
/// passing through; a ship flying in through the hangar's door without
/// touching it; rounds stopped on it; and the capsules round it seen
/// by a pilot's lidar, unless the pilot means to go in.
/// Needs the Synty files (tools/import_synty.cmake).
/// Run: Godot --headless --fixed-fps 60 --path . res://tests/station_test.tscn
/// Exits with 0 if every check passed.
/// </summary>
public partial class StationTest : Node
{
    private int _failures;

    public override async void _Ready()
    {
        var world = new Node3D { Name = "World" };
        AddChild(world);
        var station = GD.Load<PackedScene>("res://scenes/stations/synty_station.tscn").Instantiate<Node3D>();
        station.Transform = new Transform3D(new Basis(Vector3.Up, 0.5f), new Vector3(0, 0, -2000));
        world.AddChild(station);
        await Frames(2);

        Solid[] solids = Solid.All.Where(s => station.IsAncestorOf(s)).ToArray();
        int triangles = solids.Sum(s => s.A.Length);
        Check($"every module and part is solid ({solids.Length} solids, {triangles} triangles)",
              solids.Length == 25 && triangles > 4000);
        int capsules = AvoidCapsule.All.Count(c => station.IsAncestorOf(c));
        Check($"every module has its capsule ({capsules})", capsules == 6);

        // The ring's outer wall, straight in from outside at its middle height
        Vector3 centre = station.ToGlobal(new Vector3(0, -72, 0));
        Vector3 outward = station.GlobalBasis * Vector3.Right;
        Vector3 start = centre + outward * 800;
        bool hit = Solid.SegmentAll(start, centre, out float along, out Vector3 normal);
        Vector3 wall = start + (centre - start) * along;
        float radius = (wall - centre).Length();
        Check($"a segment stops on the ring's outer wall ({radius:0} m from the axis, normal {normal.Dot(outward):0.00} outward)",
              hit && radius > 250 && radius < 290 && normal.Dot(outward) > 0.7f);

        var rounds = new Projectiles { Name = "Projectiles" };
        world.AddChild(rounds);
        var ship = new ShipBody { Name = "Ship", Position = new Vector3(0, 0, 3000) };
        ship.AddChild(new ShipComputer { Name = "ShipComputer" });
        ship.AddChild(GD.Load<PackedScene>("res://scenes/ships/fighter_03_hitboxes.tscn").Instantiate<Node3D>());
        ship.GetNode<Node3D>("Hitboxes").Transform = new Transform3D(new Basis(Vector3.Up, Mathf.Pi), Vector3.Zero);
        var hull = new ShipHull { Name = "Hull", Integrity = 1e7f, Insured = false };
        ship.AddChild(hull);
        world.AddChild(ship);
        await Frames(2);

        // Into the outer wall head on, and down onto the ring's top
        Vector3 top = station.ToGlobal(new Vector3(200, 200, 0));
        Vector3 down = -station.GlobalBasis.Y;
        Solid.SegmentAll(top, top + down * 400, out float t, out _);
        Vector3 deck = top + down * 400 * t;
        foreach ((string what, Vector3 from, Vector3 to) in new[] { ("the ring's outer wall", start, wall), ("the ring's top", top, deck) })
            foreach (float speed in new[] { 20f, 150f, 400f })
            {
                Vector3 way = (to - from).Normalized();
                Quaternion nose = Basis.LookingAt(way, Mathf.Abs(way.Y) > 0.9f ? Vector3.Right : Vector3.Up).GetRotationQuaternion();
                float before = hull.CrashDamage;
                ship.State = new ShipState { Position = from, Orientation = nose, Velocity = way * speed, AngularVelocity = Vector3.Zero };
                int frames = (int)((from.DistanceTo(to) / speed * 1.5f + 3f) * 60);     // the veil slows it on the way
                float deepest = float.MaxValue;
                for (int f = 0; f < frames; f++)
                {
                    await Frames(1);
                    deepest = Mathf.Min(deepest, (to - ship.GlobalPosition).Dot(way));
                }
                Check($"at {speed} m/s into {what} the ship bounces off ({ship.LinearVelocity.Dot(way):0.0} m/s on, its middle {deepest:0.0} m short of it)",
                      ship.LinearVelocity.Dot(way) < 0.5f && deepest > 0);
                Check($"and the crash takes from the hull ({hull.CrashDamage - before:0} points)", hull.CrashDamage > before);
            }

        // In through each hangar's door, along the room, to the control room
        // at its back
        for (int n = 1; n <= 4; n++)
        {
            var hangar = station.GetNode<Node3D>($"HangarBig{n}");
            Vector3 inward = -hangar.GlobalBasis.Z;
            Vector3 from = hangar.ToGlobal(new Vector3(0, 10, 66));
            float before = hull.CrashDamage;
            ship.State = new ShipState
            {
                Position = from, Orientation = Basis.LookingAt(inward, hangar.GlobalBasis.Y).GetRotationQuaternion(),
                Velocity = inward * 20, AngularVelocity = Vector3.Zero,
            };
            float firstCrash = float.NaN;
            for (int f = 0; f < 60 * 12; f++)
            {
                await Frames(1);
                if (float.IsNaN(firstCrash) && hull.CrashDamage > before)
                    firstCrash = (ship.GlobalPosition - from).Dot(inward);
            }
            Check($"a ship flying in through hangar {n}'s door first touches its control room ({firstCrash:0.0} m in)",
                  firstCrash > 120 && firstCrash < 145);
        }

        // A round fired at it goes no farther
        ship.State = new ShipState { Position = start + new Vector3(0, 3000, 0), Orientation = Quaternion.Identity };
        rounds.Fire(start, (wall - start).Normalized() * 1200, 10);
        await Frames(2);
        int flying = rounds.Flying;
        await Frames((int)(start.DistanceTo(wall) / 1200 * 60) + 5);
        Check($"a round fired at the station stops on it ({flying} then {rounds.Flying} flying)", flying == 1 && rounds.Flying == 0);

        // A lidar sees the capsules; going in, it looks past them (the walls stay solid)
        var eyes = new RockSensor { Name = "Eyes", Range = 1500, Position = centre + outward * 1200 };
        world.AddChild(eyes);
        await Frames(1);
        eyes.Look(-outward, 1);
        Check($"a lidar 1.2 km out sees the station's capsule ahead (clear {eyes.Clear[0]:0} m)", eyes.Clear[0] < 1000);
        Vector3 steer = eyes.Steer(-outward, 1200);
        Check($"and steers round it ({Mathf.RadToDeg(steer.AngleTo(-outward)):0} degrees off)", eyes.Avoiding && steer.Dot(-outward) < 0.99f);
        eyes.Entering = station;
        eyes.Look(-outward, 1);
        Check($"going in, it looks past the capsules (clear {eyes.Clear[0]:0} m)", eyes.Clear[0] >= eyes.Range);
        var inside = new RockSensor { Name = "Inside", Range = 1500, Position = station.ToGlobal(new Vector3(0, -350, 0)) };
        world.AddChild(inside);
        await Frames(1);
        inside.Look(-station.GlobalBasis.Y, 1);
        Check($"a lidar inside a capsule sees the way out clear ({inside.Clear[0]:0} m)", inside.Clear[0] >= inside.Range);

        System.GC.Collect();
        System.GC.WaitForPendingFinalizers();
        GetTree().Quit(_failures == 0 ? 0 : 1);
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
