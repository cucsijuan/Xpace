using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Xpace.Net;
using Xpace.Ship;
using Xpace.Space;

namespace Xpace.Tests;

/// <summary>
/// Ships the game brings in while playing (a <see cref="ShipSpawner"/> of AI
/// fighters), across a session. First this scene is the host and two guests
/// are plain code over a simulated network: the guests hear of each ship
/// brought in (also the one brought in before the session started), follow
/// it, hear when a wreck is taken away, and the guest who comes late hears
/// of those still flying. Then the scene is a guest: what its spawner
/// brought in before joining goes, it brings in nothing more, and the host's
/// spawned ship shows here as a puppet.
/// Run: Godot --headless --fixed-fps 60 --path . res://tests/spawn_test.tscn
/// Exits with 0 if every check passed.
/// </summary>
public partial class SpawnTest : Node
{
    private const float Dt = 1f / 60;
    private const string Fighter = "res://scenes/ships/ai_fighter.tscn";
    private int _failures;

    /// <summary>A ship on a machine that is plain code.</summary>
    private sealed class Fake : INetEntity
    {
        public ushort NetId { get; init; }
        public bool IsLocal { get; init; }
        public RemoteTrack Track { get; init; }
        public Hitbox[] Hitboxes => System.Array.Empty<Hitbox>();
        public SectorPoint Position;
        public bool Ended;

        public ShipSnapshot Snapshot(uint tick) => new() { Tick = tick, Position = Position, Orientation = Quaternion.Identity };

        public void TakeHit(HitEvent hit) { }
    }

    public override async void _Ready()
    {
        await AsHost();
        await AsGuest();
        GD.Print(_failures == 0 ? "ALL PASSED" : $"{_failures} FAILED");
        GetTree().Quit(_failures == 0 ? 0 : 1);
    }

    private (FloatingOrigin, SessionNode, ShipSpawner) World(int count, double first)
    {
        var origin = new FloatingOrigin { Name = "World" };
        AddChild(origin);
        origin.AddChild(new SectorClock());
        origin.AddChild(new Projectiles());
        var session = new SessionNode();
        session.Spawnables.Add(GD.Load<PackedScene>(Fighter));
        origin.AddChild(session);
        var spawner = new ShipSpawner
        {
            Name = "Raiders", Scene = GD.Load<PackedScene>(Fighter), Count = count, FirstSeconds = first,
            IntervalSeconds = 0.5, WreckSeconds = 1, Radius = 300, Position = new Vector3(0, 0, -3000),
        };
        origin.AddChild(spawner);
        return (origin, session, spawner);
    }

    private static Session Guest(SimulatedNetwork net, byte peer, Dictionary<ushort, Fake> seen)
    {
        var guest = new Session(peer, net.Join(peer)) { TickSeconds = Dt };
        guest.SpawnDynamic = (id, scene) =>
        {
            if (scene != 0)
                return null;
            var copy = new Fake { NetId = id, Track = new RemoteTrack { TickSeconds = Dt } };
            seen[id] = copy;
            return copy;
        };
        guest.Despawned += (e, tick, exploded) =>
        {
            ((Fake)e).Ended = true;
            guest.Remove(e.NetId);
        };
        return guest;
    }

    private async Task AsHost()
    {
        (FloatingOrigin origin, SessionNode host, ShipSpawner spawner) = World(2, 0);
        var net = new SimulatedNetwork { Latency = 0.06, Jitter = 0.02, Loss = 0.05 };
        var seen1 = new Dictionary<ushort, Fake>();
        var seen2 = new Dictionary<ushort, Fake>();
        Session guest1 = Guest(net, 1, seen1), guest2 = null;

        await Frames(10);
        int before = spawner.Ships.Count;
        host.Start(Session.HostPeer, net.Join(Session.HostPeer));
        Node3D killed = null;
        double worst = 0;
        for (int frame = 0; frame < 600; frame++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            uint tick = SectorClock.Instance.Tick;
            net.Now = tick * Dt;
            guest1.Tick(tick);
            guest2?.Tick(tick);

            if (frame == 200)           // one of them blown up: its wreck is taken away after a second
            {
                killed = spawner.Ships.First();
                foreach (ShipHull hull in killed.FindChildren("*", "", true, false).OfType<ShipHull>())
                    hull.Hit(1e6f);
            }
            if (frame == 420)
                guest2 = Guest(net, 2, seen2);
            if (frame > 100 && frame < 200)
                foreach (Node3D ship in spawner.Ships)
                    if (host.IdOf(ship) is ushort id && seen1.GetValueOrDefault(id)?.Track.ShownTick is double shown)
                        worst = System.Math.Max(worst, ship.GlobalPosition.DistanceTo(origin.Origin.To(seen1[id].Track.Sample(shown).Position)));
        }

        ushort[] ids = seen1.Keys.ToArray();
        Check($"one ship was brought in before the session ({before}), it joins when it starts",
              before == 1 && ids.Length >= 2);
        Check($"guest 1 heard of {ids.Length} ships brought in, all the host's, made while playing ({string.Join(", ", ids.Select(i => $"{i:X4}"))})",
              ids.Length == 3 && ids.All(i => NetIds.Owner(i) == Session.HostPeer && NetIds.IsDynamic(i)));
        Check($"guest 1 follows them (at most {worst:0.0} m from where the host's are, shown a little late)",
              worst > 0 && worst < 60);
        Check("the wreck taken away ended on guest 1", killed != null && !IsInstanceValid(killed) && seen1.Values.Count(f => f.Ended) == 1);
        Check($"the spawner keeps {spawner.Count} alive: a new one came", spawner.Ships.Count == 2);
        Check($"guest 2, come late, heard of the {seen2.Count} flying", seen2.Count == 2 && seen2.Keys.All(k => !seen1[k].Ended));

        origin.QueueFree();
        await Frames(2);
    }

    private async Task AsGuest()
    {
        (FloatingOrigin origin, SessionNode scene, ShipSpawner spawner) = World(1, 0);
        var net = new SimulatedNetwork { Latency = 0.04, Jitter = 0.01, Loss = 0 };
        var host = new Session(Session.HostPeer, net.Join(Session.HostPeer)) { TickSeconds = Dt };

        await Frames(10);
        int before = spawner.Ships.Count;
        scene.Start(1, net.Join(1));
        var hosts = new Fake { NetId = (ushort)(NetIds.PlayerShip(Session.HostPeer) | NetIds.FirstDynamic), IsLocal = true, Position = new SectorPoint(100, 0, -2000) };
        host.Spawn(hosts, 0);
        int most = 0;
        for (int frame = 0; frame < 240; frame++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            uint tick = SectorClock.Instance.Tick;
            net.Now = tick * Dt;
            host.Tick(tick);
            most = System.Math.Max(most, spawner.Ships.Count);
        }

        Node3D puppet = scene.NodeOf(hosts.NetId);
        Check($"the guest's spawner had brought one in ({before}); it went when the session started, and none came after",
              before == 1 && most == 0 && spawner.Ships.Count == 0);
        Check($"the host's spawned ship shows here as a puppet ({puppet?.GlobalPosition.DistanceTo(origin.Origin.To(hosts.Position)) ?? -1:0.00} m from it)",
              puppet != null && NetBody.IsPuppet(puppet) && puppet.Visible && puppet.GlobalPosition.DistanceTo(origin.Origin.To(hosts.Position)) < 0.1f);

        origin.QueueFree();
        await Frames(2);
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
