using System;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Xpace.Net;
using Xpace.Ship;
using Xpace.Space;

namespace Xpace.Tests;

/// <summary>
/// The game's fighter pilot (FighterAi) in its ship, with its own computer
/// running FLIGHT, FIRECTL and RWR. It is told nothing of its enemies:
///
///   - a friend (on its datalink) and a stranger ahead: it finds the
///     stranger on its radar, locks it (moving the lock off the friend if
///     it falls there), hits it with the gun and lets a radar missile go;
///     the friend is never shot;
///   - with nothing on its radar it searches where the stranger was, then
///     patrols round where it started;
///   - a heat seeker flying past it, for someone else: it carries on; one
///     coming at it: it breaks and throws flares;
///   - a radar guiding a missile at it: it breaks and throws chaff;
///   - blown up: its hands come off the controls; the insurer's new ship
///     flies again.
///
/// Run: Godot --headless --fixed-fps 60 --path . res://tests/duel_test.tscn
/// Exits with 0 if every check passed.
/// </summary>
public partial class DuelTest : Node
{
    private int _failures;

    public override async void _Ready()
    {
        var world = new Node3D { Name = "World" };
        AddChild(world);
        world.AddChild(new Projectiles());
        var crossing = new Basis(Vector3.Up, -Mathf.Pi / 2);
        var stranger = new Drone
        {
            Name = "Stranger", Straight = true, Evades = false, Defends = false, Speed = 120, Health = 100_000,
            Transform = new Transform3D(crossing, new Vector3(-400, 150, -3000)),
        };
        world.AddChild(stranger);
        var friend = new Drone
        {
            Name = "Friend", Straight = true, Evades = false, Defends = false, Speed = 120, Health = 100_000,
            Transform = new Transform3D(crossing, new Vector3(-200, 50, -2000)),
        };
        friend.AddChild(new Datalink { Channel = "raiders" });
        world.AddChild(friend);
        var ai = GD.Load<PackedScene>("res://scenes/ships/ai_fighter.tscn").Instantiate<ShipBody>();
        world.AddChild(ai);
        var pilot = ai.GetNode<FighterAi>("Pilot");
        var radar = ai.GetNode<RadarDevice>("Radar");
        var launcher = ai.GetNode<LauncherDevice>("Launcher");
        var dispenser = ai.GetNode<DispenserDevice>("Dispenser");
        var hull = ai.GetNode<ShipHull>("Hull");

        bool booted = await Until(() => ai.Computer.ScreenText().Contains("Threat warning resident"), 60);
        Check("its computer is up: FLIGHT, FIRECTL and RWR resident", booted);

        // The attack: it has to find the stranger itself, and leave the friend alone
        bool lockedFriend = false, lockedStranger = false;
        float closest = float.MaxValue;
        for (int i = 0; i < 90 * 60 && (stranger.Hits < 10 || launcher.Launched == 0); i++)
        {
            await Frames(1);
            lockedFriend |= radar.Locked == friend;
            lockedStranger |= pilot.Target == stranger;
            closest = Math.Min(closest, ai.GlobalPosition.DistanceTo(stranger.GlobalPosition));
        }
        Check($"it finds the stranger with its own radar and locks it ({lockedStranger}, closing to {closest:0} m)", lockedStranger && closest < 1500);
        Check($"its gun hits it ({stranger.Hits} hits) and it lets a radar missile go ({launcher.Launched})", stranger.Hits >= 10 && launcher.Launched >= 1);
        Check($"the friend on its datalink is never shot ({friend.Hits} hits{(lockedFriend ? "; the lock fell on it and was moved off" : "")})",
              friend.Hits == 0);

        // Nothing on its radar: it searches, then patrols
        stranger.QueueFree();
        friend.QueueFree();
        bool searched = await Until(() => pilot.Doing == "searching", 10);
        bool patrolled = await Until(() => pilot.Doing == "patrolling", 30);
        Check($"with nothing on its radar it searches where the stranger was ({searched}), then patrols ({patrolled})", searched && patrolled);
        float far = 0;
        for (int i = 0; i < 30 * 60; i++)
        {
            await Frames(1);
            far = Math.Max(far, ai.GlobalPosition.Length());
        }
        GD.Print($"     patrolling, it keeps within {far:0} m of where it started");

        // A heat seeker flying past, for someone else
        int flares = dispenser.FlaresLeft;
        Vector3 side = ai.GlobalPosition + ai.GlobalBasis.X * 1500 + ai.GlobalBasis.Z * 1500;
        Missile past = Seeker(world, side, -ai.GlobalBasis.Z, ai.LinearVelocity);
        bool brokeForOther = false;
        for (int i = 0; i < 4 * 60 && IsInstanceValid(past); i++)
        {
            await Frames(1);
            brokeForOther |= pilot.Doing.StartsWith("breaking");
        }
        Check($"a heat seeker flying past, at no one: it carries on ({pilot.Doing})", !brokeForOther && dispenser.FlaresLeft == flares);
        if (IsInstanceValid(past))
            past.QueueFree();

        // A heat seeker coming at it
        Vector3 behind = ai.GlobalPosition + ai.GlobalBasis.Z * 2000;
        Missile seeker = Seeker(world, behind, (ai.GlobalPosition - behind).Normalized(), ai.LinearVelocity);
        seeker.Target = ai.GetNode<ShipSignature>("Signature");
        bool brokeFlares = false;
        for (int i = 0; i < 60 * 12 && IsInstanceValid(seeker); i++)
        {
            await Frames(1);
            brokeFlares |= pilot.Doing == "breaking (flares)";
        }
        Check($"a heat seeker coming at it: it breaks ({brokeFlares}) and throws flares ({flares - dispenser.FlaresLeft})",
              brokeFlares && dispenser.FlaresLeft < flares);
        GD.Print($"     the seeker {(hull.Losses > 0 || hull.Left < hull.Integrity ? "got it" : "was decoyed or outflown")} (hull {hull.Left:0} of {hull.Integrity:0})");
        await Until(() => !pilot.Doing.StartsWith("breaking"), 10);

        // A radar guiding a missile at it
        int chaff = dispenser.ChaffLeft;
        var hunter = new Node3D { Name = "Hunter", Position = ai.GlobalPosition + ai.GlobalBasis.X * 3000 };
        world.AddChild(hunter);
        var beam = new RadarEmitter { Signature = "F", PowerAt1Km = 4e4f };
        hunter.AddChild(beam);
        hunter.LookAt(ai.GlobalPosition, Vector3.Up);
        bool brokeChaff = false;
        for (int i = 0; i < 5 * 60; i++)
        {
            beam.Follow(RadarMode.Track, ai.GetNode<ShipSignature>("Signature"), true);
            await Frames(1);
            brokeChaff |= pilot.Doing == "breaking (chaff)";
        }
        Check($"a radar guiding a missile at it (its RWR: launch {ai.GetNode<RwrDevice>("Rwr").LaunchWarning}): it breaks ({brokeChaff}) and throws chaff ({chaff - dispenser.ChaffLeft})",
              brokeChaff && dispenser.ChaffLeft < chaff);
        hunter.QueueFree();

        // Blown up, and back
        hull.Hit(10_000);
        await Frames(5);
        Check($"blown up, it lets go of the controls ({pilot.Doing})", hull.Wrecked && pilot.Doing == "wrecked");
        await Until(() => !hull.Wrecked, hull.RespawnSeconds + 2);
        await Frames(5);
        Check("the insurer's new ship flies again", !hull.Wrecked && pilot.Doing != "wrecked");

        GD.Print(_failures == 0 ? "ALL PASSED" : $"{_failures} FAILED");
        GetTree().Quit(_failures == 0 ? 0 : 1);
    }

    private static Missile Seeker(Node world, Vector3 from, Vector3 way, Vector3 carried)
    {
        var m = GD.Load<PackedScene>("res://scenes/weapons/ir_missile.tscn").Instantiate<Missile>();
        world.AddChild(m);
        m.GlobalPosition = from;
        m.LookAt(from + way, Vector3.Up);
        m.Velocity = way * 300 + carried;
        m.SeekerLook = way;
        return m;
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

    private void Check(string what, bool ok)
    {
        GD.Print((ok ? "ok   " : "FAIL ") + what);
        if (!ok)
            _failures++;
    }
}
