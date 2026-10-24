using System;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Xpace.Computer;
using Xpace.Net;
using Xpace.Ship;
using Xpace.Space;

namespace Xpace.Tests;

/// <summary>
/// The ship's hull: rounds fired at it hit its hitboxes (single player, the
/// physics engine's areas) and wear the hull away; its own rounds leave it
/// alone; a warhead's blast hurts it too. With nothing left it blows up: a
/// wreck that drifts, with no echo, no heat and no thrust, and a session's
/// snapshots say it is destroyed. The insurer's new ship comes back where
/// it started, with a full hull, chaff and flares.
/// Run: Godot --headless --fixed-fps 60 --path . res://tests/hull_test.tscn
/// Exits with 0 if every check passed.
/// </summary>
public partial class HullTest : Node
{
    private int _failures;

    public override async void _Ready()
    {
        var world = new Node3D { Name = "World" };
        AddChild(world);
        var rounds = new Projectiles();
        world.AddChild(rounds);
        var ship = new ShipBody { Name = "Ship", Position = new Vector3(0, 0, -100) };
        ship.AddChild(new ShipComputer { Name = "ShipComputer" });
        ship.AddChild(GD.Load<PackedScene>("res://scenes/ships/fighter_03_hitboxes.tscn").Instantiate<Node3D>());
        ship.GetNode<Node3D>("Hitboxes").Transform = new Transform3D(new Basis(Vector3.Up, Mathf.Pi), Vector3.Zero);
        var signature = new ShipSignature { Name = "Signature" };
        ship.AddChild(signature);
        var hull = new ShipHull { Name = "Hull", Integrity = 100, RespawnSeconds = 2 };
        ship.AddChild(hull);
        var dispenser = new DispenserDevice { Name = "Dispenser", Chaff = 4, Flares = 4 };
        ship.AddChild(dispenser);
        var body = new NetBody { OwnedBy = NetOwner.Pilot };
        ship.AddChild(body);
        world.AddChild(ship);
        await Frames(2);
        body.Join(0, true, 1f / 60);

        // Rounds from 1 km ahead, along the ship's axis: 10 damage each
        for (int i = 0; i < 5; i++)
            rounds.Fire(new Vector3(0, -0.8f, -1100), new Vector3(0, 0, 1200), 10);
        await Frames(70);
        Check($"rounds fired at the ship hit it ({signature.Hits} of 5) and wear the hull ({hull.Left:0} of 100 left)",
              signature.Hits == 5 && Math.Abs(hull.Left - 50) < 0.01f);

        // Its own rounds, from inside its hull, go out without touching it
        for (int i = 0; i < 5; i++)
            rounds.Fire(ship.GlobalPosition + new Vector3(0.9f, 0.8f, -3), new Vector3(0, 0, -1200), 10, ship);
        await Frames(10);
        Check($"its own rounds leave it alone ({hull.Left:0} left)", Math.Abs(hull.Left - 50) < 0.01f);

        // A round just over the wing, 3 m out from the fuselage, misses
        rounds.Fire(new Vector3(3, 2.6f, -1100), new Vector3(0, 0, 1200), 10);
        await Frames(70);
        Check($"a round just over the wing misses ({hull.Left:0} left)", Math.Abs(hull.Left - 50) < 0.01f);

        // A missile blows up 20 m off: the fragments hurt, not kill
        var missile = new Missile { Name = "Missile", Damage = 150, LethalRadius = 6, BlastRadius = 25 };
        signature.Hit(DamageAt(missile, 20));
        Check($"a warhead 20 m off hurts it ({hull.Left:0} left)", hull.Left < 50 && hull.Left > 0);
        missile.Free();

        dispenser.Dispense(ship, true, true);          // some used before it is lost
        Check("(the dispenser has thrown one of each)", dispenser.ChaffLeft == 3 && dispenser.FlaresLeft == 3);
        for (int i = 0; i < 5; i++)
            rounds.Fire(new Vector3(0, -0.8f, -1100), new Vector3(0, 0, 1200), 10);
        await Frames(70);
        Check($"with nothing left the ship blows up: a wreck ({signature.Hits} hits)", hull.Wrecked && ship.Wrecked && hull.Losses == 1);
        ship.LinearVelocity = new Vector3(30, 0, 0);
        Vector3 at = ship.GlobalPosition;
        await Frames(30);
        Check($"the wreck drifts on ({ship.GlobalPosition.DistanceTo(at):0.0} m in half a second) with no thrust",
              Math.Abs(ship.GlobalPosition.DistanceTo(at) - 15) < 1 && ship.Commands.All(c => c == 0));
        Check("it shows nothing to radars or seekers", !signature.Alive && signature.HeatAt(Vector3.Zero, out _) == 0);
        Check("a session's snapshot says it is destroyed, its radar off", body.Snapshot(1) is { Destroyed: true, Radar: RadarMode.Off });
        ship.LinearVelocity = Vector3.Zero;
        int before = signature.Hits;
        rounds.Fire(new Vector3(ship.GlobalPosition.X, -0.8f, -1100), new Vector3(0, 0, 1200), 10);
        await Frames(70);
        Check("a wreck takes no more hits", signature.Hits == before);

        await Seconds(2);
        Check($"the insurer's new ship comes back where it started ({ship.GlobalPosition}), standing still, hull full",
              !hull.Wrecked && !ship.Wrecked && ship.GlobalPosition.DistanceTo(new Vector3(0, 0, -100)) < 0.5f &&
              ship.LinearVelocity == Vector3.Zero && hull.Left == 100);
        Check("with a full dispenser", dispenser.ChaffLeft == 4 && dispenser.FlaresLeft == 4);
        Check("and its signature is back", signature.Alive && body.Snapshot(2) is { Destroyed: false });

        GD.Print(_failures == 0 ? "ALL PASSED" : $"{_failures} FAILED");
        GetTree().Quit(_failures == 0 ? 0 : 1);
    }

    private static float DamageAt(Missile m, float d)
    {
        float r = MathF.Max(d, m.LethalRadius) / m.LethalRadius;
        return d >= m.BlastRadius ? 0 : m.Damage / (r * r);
    }

    private async Task Frames(int n)
    {
        for (int i = 0; i < n; i++)
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
    }

    private Task Seconds(double s) => Frames((int)Math.Ceiling(s * 60) + 2);

    private void Check(string what, bool ok)
    {
        GD.Print((ok ? "ok   " : "FAIL ") + what);
        if (!ok)
            _failures++;
    }
}
