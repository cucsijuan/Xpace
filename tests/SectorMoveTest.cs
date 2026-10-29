using System;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Xpace.Computer;
using Xpace.Ship;
using Xpace.Space;

namespace Xpace.Tests;

/// <summary>
/// A ship moves from one sector to another whole: each sector is its own world
/// (a SubViewport with its own World3D), and the ship is taken out of the first
/// and put into the second without being rebuilt. Its computer runs on (the
/// same machine, its time going on, its programs resident), its engine's
/// controller and its launcher's rail head are the same, its hull still feels
/// crashes, and it flies in the new world's physics.
/// Run: Godot --headless --fixed-fps 60 --path . res://tests/sector_move_test.tscn
/// Exits with 0 if every check passed.
/// </summary>
public partial class SectorMoveTest : Node
{
    private int _failures;

    public override async void _Ready()
    {
        FloatingOrigin a = Sector("A"), b = Sector("B");
        var ship = GD.Load<PackedScene>("res://scenes/ships/ai_fighter.tscn").Instantiate<ShipBody>();
        ship.GetNode("Pilot").ProcessMode = ProcessModeEnum.Disabled;    // nobody at the controls
        a.AddChild(ship);
        // Sector A has a drone ahead and a wall where the ship will be pushed in sector B
        a.AddChild(new Drone { Name = "Drone", Position = new Vector3(0, 0, -1500), Speed = 0 });
        a.AddChild(Wall(-125));
        var computer = ship.GetNode<ShipComputer>("ShipComputer");
        var hull = ship.GetNode<ShipHull>("Hull");
        await Frames(60 * 20);                       // boots and loads its programs

        double before = computer.EmulatedSeconds;
        string screen = computer.ScreenText();
        Check($"the computer is running in sector A ({before:0.0} s)", before > 10);
        Check("the drone is among the radar targets of the ship's sector", Targets(ship).Contains("Drone"));

        Sectors.Move(ship, b, new Transform3D(Basis.Identity, new Vector3(0, 0, -50)));
        await Frames(2);
        Check("the ship is in sector B's world", ship.GetWorld3D() == b.GetWorld3D() && ship.GetWorld3D() != a.GetWorld3D());
        Check($"the same computer goes on ({before:0.0} -> {computer.EmulatedSeconds:0.0} s)",
              IsInstanceValid(computer) && computer.EmulatedSeconds >= before && computer.EmulatedSeconds < before + 1);
        Check("its screen is the same", computer.ScreenText() == screen);

        await Frames(120);
        Check($"and keeps running ({computer.EmulatedSeconds:0.0} s)", computer.EmulatedSeconds > before + 1.5);

        Check("sector A's drone is not among its targets now", !Targets(ship).Contains("Drone") && Targets(ship).Contains((string)ship.Name));

        // Sector A's wall is not in sector B
        ship.GlobalTransform = new Transform3D(Basis.Identity, new Vector3(0, 0, -100));
        ship.LinearVelocity = new Vector3(0, 0, -60);
        await Frames(90);
        Check($"sector A's wall is not in its way ({hull.CrashDamage:0.0})", hull.CrashDamage == 0);

        // The new world's physics: flight assist brakes a push, and the ship crashes into a wall there
        b.AddChild(Wall(-75));
        ship.GlobalTransform = new Transform3D(Basis.Identity, new Vector3(0, 0, -50));
        ship.LinearVelocity = Vector3.Zero;
        await Frames(2);
        Vector3 at = ship.GlobalPosition;
        ship.LinearVelocity = new Vector3(0, 0, -60);
        await Frames(90);
        Check($"it flies in sector B ({at.DistanceTo(ship.GlobalPosition):0} m, now {ship.LinearVelocity.Length():0} m/s)",
              at.DistanceTo(ship.GlobalPosition) > 5 && ship.LinearVelocity.Length() < 60);
        Check($"its hull feels a crash there ({hull.CrashDamage:0.0} of {hull.Integrity:0})", hull.CrashDamage > 0);

        // Leaving the sector for good still cleans up
        ship.QueueFree();
        await Frames(2);
        Check("a ship freed is gone", !IsInstanceValid(computer));

        GD.Print(_failures == 0 ? "ALL PASSED" : $"{_failures} FAILED");
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GetTree().Quit(_failures == 0 ? 0 : 1);
    }

    // What radars, missiles and warners of the node's sector can see: the names of the targets' nodes
    private static string[] Targets(Node from) =>
        Sectors.InGroup(from, "radar_targets").Select(n => (string)(n is ShipSignature ? n.GetParent().Name : n.Name)).ToArray();

    private static Solid Wall(float z)
    {
        var wall = new Solid { Name = "Wall", Position = new Vector3(0, 0, z) };
        wall.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(100, 100, 2) } });
        return wall;
    }

    private FloatingOrigin Sector(string name)
    {
        var view = new SubViewport { Name = name, OwnWorld3D = true, RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled };
        AddChild(view);
        var sector = new FloatingOrigin { Name = "Sector" };
        view.AddChild(sector);
        return sector;
    }

    private async Task Frames(int n)
    {
        for (int i = 0; i < n; i++)
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
    }

    private void Check(string what, bool ok)
    {
        GD.Print($"{(ok ? "ok  " : "FAIL")} {what}");
        if (!ok)
            _failures++;
    }
}
