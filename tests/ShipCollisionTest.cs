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
/// Ships run into each other: head on, a fighter against a freighter, a bump that hurts nobody, at cruise speed, into a
/// ship held on a pad, into a ship flown on another machine; and a pilot sees a ship ahead and steers round it.
///
/// Run: Godot --headless --fixed-fps 60 --path . res://tests/ship_collision_test.tscn
/// </summary>
public partial class ShipCollisionTest : Node3D
{
    private sealed class Clamp : IShipHold
    {
        public Vector3 At;
        public ShipState Hold(ShipBody ship, double delta) => new() { Position = At, Orientation = Quaternion.Identity };
    }

    private int _failures, _blows;

    public override async void _Ready()
    {
        var root = new FloatingOrigin();
        AddChild(root);
        Combat.Damaged += Blow;

        // Head on, alike: they stop and bounce back, and both pay the same
        (ShipBody a, ShipBody b) = Pair("ai_fighter", "ai_fighter", root, 100, 30, 30);
        await Until(() => V(a) <= 0, 6);
        await Seconds(0.1);
        Check($"head on they bounce ({V(a):0.0} and {V(b):0.0} m/s)", V(a) < 0 && V(b) > 0);
        Check("alike, by the same", Mathf.Abs(V(a) + V(b)) < 1);
        float hurtA = a.Find<ShipHull>().CrashDamage, hurtB = b.Find<ShipHull>().CrashDamage;
        Check($"both hulls take it, the same ({hurtA:0} and {hurtB:0})", hurtA > 0 && Mathf.Abs(hurtA - hurtB) < hurtA * 0.01f);
        Check("a hard crash is somebody's blow", _blows > 0);
        Free(a, b);

        // A fighter into a freighter
        (a, b) = Pair("ai_fighter", "ai_freighter", root, 120, 40, 0);
        float fighterMass = a.SolidMass, freighterMass = b.SolidMass;
        float before = fighterMass * V(a) + freighterMass * V(b);
        await Until(() => V(a) <= 12, 6);
        await Seconds(0.1);
        float after = fighterMass * V(a) + freighterMass * V(b);
        Check($"the fighter loses more speed than the freighter gets ({V(a):0.0}, {V(b):0.0} m/s)", 40 - V(a) > 2 * V(b) && V(b) > 0);
        Check($"the push goes from one to the other ({before:0} and {after:0} kg m/s)", MathF.Abs(after - before) < MathF.Abs(before) * 0.2f);
        Free(a, b);

        // A bump
        _blows = 0;
        (a, b) = Pair("ai_fighter", "ai_fighter", root, 16, 1, 1);
        await Until(() => V(a) <= 0.01f, 12);
        Check($"a bump does next to nothing ({a.Find<ShipHull>().Fraction:0.000} of the hull left), and is nobody's fault", a.Find<ShipHull>().Fraction > 0.95f && _blows == 0);
        Free(a, b);

        // At cruise speed: the way between steps is checked
        (a, b) = Pair("ai_fighter", "ai_fighter", root, 400, 3000, 0);
        await Seconds(0.5);
        Check($"at 3 km/s it doesn't go through ({P(a) - P(b):0} m, {a.Find<ShipHull>().CrashDamage:0} of damage)", a.Find<ShipHull>().CrashDamage > 0 && P(a) < P(b));
        Free(a, b);

        // Into a ship on a pad
        (a, b) = Pair("ai_fighter", "ai_freighter", root, 120, 30, 0);
        Vector3 pad = b.GlobalPosition;
        b.Holder = new Clamp { At = pad };
        b.LinearVelocity = Vector3.Zero;
        await Until(() => V(a) <= 0, 6);
        await Seconds(0.2);
        Check($"a ship held doesn't move ({b.GlobalPosition.DistanceTo(pad):0.00} m)", b.GlobalPosition.DistanceTo(pad) < 0.01f);
        Check($"all of the bounce is the other's ({V(a):0.0} m/s)", V(a) < -5);
        Check($"and its hull takes its share ({b.Find<ShipHull>().CrashDamage:0})", b.Find<ShipHull>().CrashDamage > 0);
        Free(a, b);

        // A ship flown on another machine: only this side is worked out here
        var remote = GD.Load<PackedScene>("res://scenes/ships/remote_fighter.tscn").Instantiate<RemoteShip>();
        root.AddChild(remote);
        a = Make("ai_fighter", root);
        remote.GlobalPosition = new Vector3(0, 0, 0);
        remote.Visible = true;                      // (hidden until its first snapshot)
        a.State = new ShipState { Position = new Vector3(0, 0, 40), Orientation = Quaternion.Identity, Velocity = Vector3.Forward * 30 };
        await Until(() => V(a) <= 0, 6);
        Check($"a ship runs into one flown elsewhere and can't go through ({V(a):0.0} m/s)", V(a) < 1);
        Check("that one stays as its owner says", remote.GlobalPosition.Length() < 0.01f);
        a.QueueFree();
        remote.QueueFree();
        await Seconds(0.1);

        // A pilot sees a ship in the way
        var eyes = new RockSensor();
        var pilotShip = Make("ai_fighter", root);
        pilotShip.GetNode<RockSensor>("RockSensor").Free();
        pilotShip.AddChild(eyes);
        var parked = Make("ai_freighter", root);
        pilotShip.State = new ShipState { Position = Vector3.Zero, Orientation = Quaternion.Identity };
        parked.State = new ShipState { Position = new Vector3(0, 0, -200), Orientation = Quaternion.Identity };
        await Seconds(0.1);
        eyes.Look(Vector3.Forward, 1);
        Check($"a ship ahead shortens what is clear ({eyes.ClearAlong(Vector3.Forward):0} m)", eyes.ClearAlong(Vector3.Forward) < 200);
        Check("it is no rock", !eyes.RocksNear);
        Vector3 way = eyes.Steer(Vector3.Forward, 300);
        Check($"and it steers round it ({way})", eyes.Avoiding && way.AngleTo(Vector3.Forward) > 0.05f);

        Combat.Damaged -= Blow;
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GD.Print(_failures == 0 ? "ALL PASSED" : $"{_failures} FAILED");
        GetTree().Quit(_failures == 0 ? 0 : 1);
    }

    private void Blow(Node3D victim, Node3D by) => _blows++;

    private static ShipBody Make(string scene, Node root)
    {
        var ship = GD.Load<PackedScene>($"res://scenes/ships/{scene}.tscn").Instantiate<ShipBody>();
        foreach (ShipComputer c in ship.FindChildren("*", "", true, false).OfType<ShipComputer>())
            c.PowerOnAtStart = false;
        ship.GetNode("Pilot").ProcessMode = ProcessModeEnum.Disabled;
        ship.ControlsSource = () => default;
        root.AddChild(ship);
        ship.ControlsSource = () => default;
        return ship;
    }

    // Along the line two ships meet on, each flying nose first: the veil holds a ship going sideways
    private static float V(ShipBody ship) => -ship.LinearVelocity.Z;

    private static float P(ShipBody ship) => -ship.GlobalPosition.Z;

    // Two ships nose to nose on one line, each at its speed
    private static (ShipBody, ShipBody) Pair(string first, string second, Node root, float apart, float speed, float otherSpeed)
    {
        ShipBody a = Make(first, root), b = Make(second, root);
        a.State = new ShipState { Position = new Vector3(0, 0, apart / 2), Orientation = Quaternion.Identity, Velocity = Vector3.Forward * speed };
        b.State = new ShipState { Position = new Vector3(0, 0, -apart / 2), Orientation = new Quaternion(Vector3.Up, Mathf.Pi), Velocity = Vector3.Back * otherSpeed };
        return (a, b);
    }

    private static void Free(ShipBody a, ShipBody b)
    {
        a.QueueFree();
        b.QueueFree();
    }

    private async Task Seconds(double s)
    {
        for (int i = 0; i < s * 60; i++)
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
    }

    private async Task<bool> Until(Func<bool> done, double seconds)
    {
        for (int i = 0; i < seconds * 60; i++)
        {
            if (done())
                return true;
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        }
        return done();
    }

    private void Check(string what, bool ok)
    {
        GD.Print($"{(ok ? "ok  " : "FAIL")} {what}");
        if (!ok)
            _failures++;
    }
}
