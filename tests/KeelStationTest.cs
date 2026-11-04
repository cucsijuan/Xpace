using System;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Xpace.Computer;
using Xpace.OnFoot;
using Xpace.Ship;
using Xpace.Space;

namespace Xpace.Tests;

/// <summary>
/// The small station on a keel, with its hangar in a pod on the outside instead of cut into a ring:
///
///   - it has one dock, with the hangar's two pads, and its own colours where the other stations keep theirs;
///   - shut, the door stops the way in; the pod's walls stop a way in from the side and from above;
///   - the keel stays out of the hangar's room, and people on foot can't get through its back wall;
///   - a ship that asks is cleared, the door goes up into its housing, and the way in is clear; the housing stays solid.
///
/// Run: Godot --headless --fixed-fps 60 --path . res://tests/keel_station_test.tscn
/// Exits with 0 if every check passed.
/// </summary>
public partial class KeelStationTest : Node3D
{
    private int _failures;

    public override async void _Ready()
    {
        var station = GD.Load<PackedScene>("res://scenes/stations/keel_station.tscn").Instantiate<Node3D>();
        station.Name = "Works";
        AddChild(station);
        var plain = GD.Load<PackedScene>("res://scenes/stations/props/hull_panel_a.tscn").Instantiate<Node3D>();
        AddChild(plain);
        StationRadio radio = station.GetNode<StationRadio>("Radio");

        var ship = new ShipBody { Spec = GD.Load<ShipSpec>("res://assets/ship/hulls/hull_fighter.tres"), Position = new Vector3(0, -90, 1500) };
        ship.AddChild(new ShipComputer { Name = "ShipComputer" });
        ship.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(5, 3, 10) } });
        var shipRadio = new RadioDevice { Name = "Radio", Callsign = "TESTER" };
        ship.AddChild(shipRadio);
        AddChild(ship);
        await Seconds(0.2);

        HangarDock[] docks = radio.Docks.ToArray();
        Check($"one dock ({docks.Length}), with two pads", docks.Length == 1 && docks[0].Pads.Count() == 2);
        HangarDock dock = docks[0];

        Material works = GD.Load<Material>("res://assets/materials/synty_station_works.tres");
        Check("the keel and the pod's panels wear the station's colours", Painted(station.GetNode("Keel")) == works && Painted(station.GetNode("HangarPod/Panels")) == works);
        Check("a panel elsewhere keeps its own", Painted(plain) != works);

        World3D world = Sectors.WorldOf(this);
        Check("shut, the door stops the way in", Obstacles.Segment(world, dock.ApproachPoint, dock.InsidePoint, out _, out _));
        Node3D pod = station.GetNode<Node3D>("HangarPod");
        Vector3 room = pod.ToGlobal(new Vector3(0, 12, -30));
        Check("the pod's side is solid", Obstacles.Segment(world, pod.ToGlobal(new Vector3(120, 12, -30)), room, out _, out _));
        Check("its roof is solid", Obstacles.Segment(world, pod.ToGlobal(new Vector3(0, 120, -30)), room, out _, out _));

        Solid keel = station.GetNode<Solid>("Keel/Solid");
        Transform3D toPod = pod.GlobalTransform.AffineInverse() * keel.Frame;
        bool Inside(Vector3 v) => Mathf.Abs(v.X) < 33.5f && v.Y > 0 && v.Y < 26 && v.Z < 15 && v.Z > -82.5f;
        int clipped = Enumerable.Range(0, keel.A.Length).Count(i => Inside(toPod * keel.A[i]) || Inside(toPod * keel.B[i]) || Inside(toPod * keel.C[i]));
        Check($"the keel stays out of the hangar ({clipped} triangles in it)", clipped == 0);

        await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        PhysicsDirectSpaceState3D space = GetWorld3D().DirectSpaceState;
        var body = new CapsuleShape3D { Radius = 0.32f, Height = 1.8f };
        int leaks = 0;
        for (float x = -33; x <= 33; x += 1)
            foreach (float y in new[] { 3.5f, 6f })
            {
                var walker = new PhysicsShapeQueryParameters3D
                {
                    Shape = body, Transform = new Transform3D(pod.GlobalBasis, pod.ToGlobal(new Vector3(x, y, -78))),
                    Motion = pod.GlobalBasis * new Vector3(0, 0, -30), CollisionMask = WalkSurface.Layer,
                };
                if (space.CastMotion(walker)[0] == 1)
                    leaks++;
            }
        Check($"nobody walks through the back wall, doors and all ({leaks} ways through)", leaks == 0);

        shipRadio.AskDocking();
        Check("a ship that asks is cleared", await Until(() => shipRadio.Dock == dock.Number, 3));
        Check("the door opens", await Until(() => dock.Door.At == 1, 14));
        bool blocked = Obstacles.Segment(world, dock.ApproachPoint, dock.InsidePoint, out float along, out _);
        Check($"open, the way in is clear{(blocked ? $" (blocked at {along:0.00})" : "")}", !blocked);
        Check("the door's housing is solid", Obstacles.Segment(world, pod.ToGlobal(new Vector3(0, 37, 80)), pod.ToGlobal(new Vector3(0, 37, -20)), out _, out _));

        GD.Print(_failures == 0 ? "ALL PASSED" : $"{_failures} FAILED");
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GetTree().Quit(_failures == 0 ? 0 : 1);
    }

    // The material on the first painted mesh below a node
    private static Material Painted(Node under) =>
        under.FindChildren("*", "MeshInstance3D", true, false).OfType<MeshInstance3D>().Select(m => m.MaterialOverride).FirstOrDefault(m => m != null);

    private async Task Seconds(double seconds)
    {
        for (int i = 0; i < Math.Max(1, (int)Math.Round(seconds * Engine.PhysicsTicksPerSecond)); i++)
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
    }

    private async Task<bool> Until(Func<bool> done, double seconds)
    {
        for (int i = 0; i < seconds * Engine.PhysicsTicksPerSecond; i++)
        {
            if (done())
                return true;
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        }
        return false;
    }

    private void Check(string what, bool ok)
    {
        GD.Print((ok ? "ok   " : "FAIL ") + what);
        if (!ok)
            _failures++;
    }
}
