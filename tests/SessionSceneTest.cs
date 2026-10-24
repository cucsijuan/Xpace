using System;
using System.Threading.Tasks;
using Godot;
using Xpace.Net;
using Xpace.Space;

namespace Xpace.Tests;

/// <summary>
/// The session inside the game, from both sides. First this scene is the
/// host (a SessionNode, the rounds, a drone flown by the game) and a guest
/// runs beside it as plain code over a simulated network: the guest shoots
/// the host's drone, the host's gun shoots the guest's ship, which appears
/// here as a RemoteShip. Then the scene is a guest: its drone is a puppet of
/// the host's ship, the player's ship here is shot by the host and shoots
/// back through Projectiles. The ships are just ships with a NetBody.
/// Run: Godot --headless --fixed-fps 60 --path . res://tests/session_scene_test.tscn
/// Exits with 0 if every check passed.
/// </summary>
public partial class SessionSceneTest : Node
{
    private const float Dt = 1f / 60;
    private const float MuzzleSpeed = 1200;
    private const byte GuestPeer = 1;
    private const ushort DroneId = SessionNode.FirstHostId;
    private static readonly ushort GuestShipId = NetIds.PlayerShip(GuestPeer);
    private static readonly Hitbox Box = new(Vector3.Zero, new Vector3(5, 3, 10));
    private int _failures;

    /// <summary>A ship on the machine that is plain code.</summary>
    private sealed class GuestShip : INetEntity
    {
        public ushort NetId { get; init; }
        public bool IsLocal { get; init; }
        public RemoteTrack Track { get; init; }
        public Hitbox[] Hitboxes => new[] { Box };
        public SectorPoint Position;
        public Vector3 Velocity;
        public bool Destroyed;
        public int Hits;

        public ShipSnapshot Snapshot(uint tick) => new()
        {
            Tick = tick, Position = Position, Orientation = Quaternion.Identity, Velocity = Velocity, Destroyed = Destroyed,
        };

        public void TakeHit(HitEvent hit) => Hits++;
    }

    /// <summary>The player's hull in the guest scene: counts what hits it.</summary>
    private partial class Hull : Node3D, IDamageable
    {
        public int Hits;

        public void Hit(float damage) => Hits++;
    }

    public override async void _Ready()
    {
        await AsHost();
        await AsGuest();
        GD.Print(_failures == 0 ? "ALL PASSED" : $"{_failures} FAILED");
        GetTree().Quit(_failures == 0 ? 0 : 1);
    }

    private async Task AsHost()
    {
        var origin = new FloatingOrigin { Name = "World" };
        AddChild(origin);
        origin.AddChild(new SectorClock());
        var rounds = new Projectiles();
        origin.AddChild(rounds);
        var drone = new Drone
        {
            Name = "Drone", Straight = true, Evades = false, Defends = false, Speed = 200, Health = 100_000,
            Transform = new Transform3D(new Basis(Vector3.Up, -Mathf.Pi / 2), new Vector3(-600, 0, -1000)),
        };
        drone.AddChild(new NetBody { OwnedBy = NetOwner.Host });
        origin.AddChild(drone);
        var host = new SessionNode();
        origin.AddChild(host);

        var net = new SimulatedNetwork { Latency = 0.06, Jitter = 0.02, Loss = 0.05 };
        host.Start(Session.HostPeer, net.Join(Session.HostPeer));
        var guest = new Session(GuestPeer, net.Join(GuestPeer)) { TickSeconds = Dt };
        var ship = new GuestShip { NetId = GuestShipId, IsLocal = true };
        guest.Add(ship);
        var seenDrone = new GuestShip { NetId = DroneId, Track = new RemoteTrack { TickSeconds = Dt } };
        guest.Add(seenDrone);                       // the host's ships are in every machine's scene
        int guestHeard = 0, guestFired = 0, hostFired = 0, mostFlying = 0;
        guest.HitConfirmed += _ => guestHeard++;

        for (int frame = 0; frame < 480; frame++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            uint tick = SectorClock.Instance.Tick;
            net.Now = tick * Dt;
            guest.Tick(tick);

            // The guest fires at the drone it sees, ten rounds a second
            if (seenDrone?.Track.ShownTick.HasValue == true && tick % 6 == 0)
            {
                RemoteTrack.Pose p = seenDrone.Track.Sample();
                Vector3 at = default(SectorPoint).To(p.Position);
                if (MathF.Abs(at.X) < 800)
                {
                    guest.Fire(tick, GuestShipId, default, Lead(at, p.Velocity) * MuzzleSpeed, 10);
                    guestFired++;
                }
            }

            // The host's gun, 600 m below, fires a round a second at the guest's ship it shows
            if (GetNodeOrNull<RemoteShip>("World/Player1") is RemoteShip shown && shown.Visible && tick % 60 == 0)
            {
                var gun = new Vector3(0, -600, 0);
                Projectiles.Instance.Fire(gun, Lead(shown.GlobalPosition - gun, shown.Velocity) * MuzzleSpeed, 5);
                hostFired++;
            }
            mostFlying = Math.Max(mostFlying, rounds.Flying);
        }
        for (int frame = 0; frame < 30; frame++)          // the last events on their way
        {
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            net.Now = SectorClock.Instance.Tick * Dt;
            guest.Tick(SectorClock.Instance.Tick);
        }

        var player = GetNodeOrNull<RemoteShip>("World/Player1");
        Check($"the guest's ship appears in the host's scene ({player?.GlobalPosition.Length() ?? -1:0.000} m from where it is)",
              player != null && player.Visible && player.GlobalPosition.Length() < 0.01f);
        Check($"the guest fired {guestFired} rounds at the host's drone, which took {drone.Hits}",
              guestFired > 40 && drone.Hits >= guestFired * 9 / 10);
        Check($"the host's {hostFired} rounds, fired through Projectiles, hit the guest's ship on the guest ({ship.Hits})",
              hostFired >= 5 && ship.Hits >= hostFired - 1);
        Check($"the guest hears every hit ({guestHeard} of {drone.Hits + ship.Hits})", guestHeard == drone.Hits + ship.Hits);
        Check($"the rounds fly here for show ({mostFlying} at most) and go when they hit ({rounds.Flying} left)",
              mostFlying > 0 && rounds.Flying == 0);
        Check("the guest's copy of the drone took no damage", seenDrone != null && seenDrone.Hits == 0);

        origin.QueueFree();
        await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
    }

    private async Task AsGuest()
    {
        var origin = new FloatingOrigin { Name = "World" };
        AddChild(origin);
        origin.AddChild(new SectorClock());
        var rounds = new Projectiles();
        origin.AddChild(rounds);
        var drone = new Drone { Name = "Drone", Health = 100_000 };    // its own mind: switched off, it follows the host
        var droneBody = new NetBody { OwnedBy = NetOwner.Host };
        drone.AddChild(droneBody);
        origin.AddChild(drone);
        var hull = new Hull { Name = "Ship" };                       // the player's ship here, standing still
        hull.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(5, 3, 10) } });
        hull.AddChild(new NetBody { OwnedBy = NetOwner.Pilot });
        origin.AddChild(hull);
        var guest = new SessionNode();
        origin.AddChild(guest);

        var net = new SimulatedNetwork { Latency = 0.06, Jitter = 0.02, Loss = 0.05 };
        var host = new Session(Session.HostPeer, net.Join(Session.HostPeer)) { TickSeconds = Dt };
        var hostShip = new GuestShip
        {
            NetId = DroneId, IsLocal = true, Position = new SectorPoint(-600, 0, -1000), Velocity = new Vector3(200, 0, 0),
        };
        host.Add(hostShip);
        GuestShip seenPlayer = null;
        host.SpawnRemote = id => id == GuestShipId ? seenPlayer = new GuestShip { NetId = id, Track = new RemoteTrack { TickSeconds = Dt } } : null;
        guest.Start(GuestPeer, net.Join(GuestPeer));

        int guestFired = 0, hostFired = 0;
        float worst = 0;
        bool hidden = false;
        var truth = new System.Collections.Generic.List<SectorPoint>();
        for (int frame = 0; frame < 540; frame++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            uint tick = SectorClock.Instance.Tick;
            net.Now = tick * Dt;
            while (truth.Count <= tick)
                truth.Add(hostShip.Position + hostShip.Velocity * ((truth.Count - (int)tick) * Dt));
            host.Tick(tick);
            hostShip.Position += hostShip.Velocity * Dt;
            hostShip.Destroyed = frame >= 480;                 // blown up at the end

            // The puppet sits where the host's ship was at the tick it shows
            if (droneBody.Track?.ShownTick is double shown && frame > 30 && frame < 470)
            {
                SectorPoint then = truth[(int)Math.Floor(shown)] + hostShip.Velocity * (float)((shown - Math.Floor(shown)) * Dt);
                worst = Math.Max(worst, drone.GlobalPosition.DistanceTo(default(SectorPoint).To(then)));
            }
            hidden = !drone.Visible;

            // This machine's player fires through Projectiles at the puppet, ten rounds a second
            if (droneBody.Track?.ShownTick.HasValue == true && tick % 6 == 0 && frame < 470 && MathF.Abs(drone.GlobalPosition.X) < 800)
            {
                Vector3 v = droneBody.Track.Sample().Velocity;
                Projectiles.Instance.Fire(Vector3.Zero, Lead(drone.GlobalPosition, v) * MuzzleSpeed, 10);
                guestFired++;
            }

            // The host's gun, 600 m below, fires at the player's ship it shows
            if (seenPlayer?.Track.ShownTick.HasValue == true && tick % 60 == 0 && frame < 470)
            {
                var gun = new SectorPoint(0, -600, 0);
                RemoteTrack.Pose p = seenPlayer.Track.Sample();
                host.Fire(tick, NetIds.PlayerShip(Session.HostPeer), gun, Lead(gun.To(p.Position), p.Velocity) * MuzzleSpeed, 5);
                hostFired++;
            }
        }

        Check($"as a guest, the drone is a puppet: its own mind is off ({!drone.IsPhysicsProcessing()}) and it follows the host's ship ({worst * 100:0.0} cm off)",
              !drone.IsPhysicsProcessing() && worst < 0.05f);
        Check($"the player here fired {guestFired} rounds through Projectiles; the host's ship took {hostShip.Hits}",
              guestFired > 40 && hostShip.Hits >= guestFired * 9 / 10);
        Check($"the host's {hostFired} rounds hit the player's ship here ({hull.Hits})", hostFired >= 5 && hull.Hits >= hostFired - 1);
        Check("the puppet's copy took no damage of its own", drone.Hits == 0);
        Check("when the host's ship is destroyed the puppet hides", hidden);

        origin.QueueFree();
        await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
    }

    private static Vector3 Lead(Vector3 p, Vector3 v)
    {
        float a = v.Dot(v) - MuzzleSpeed * MuzzleSpeed, b = 2 * p.Dot(v), c = p.Dot(p);
        float t = (-b - MathF.Sqrt(b * b - 4 * a * c)) / (2 * a);
        return (p + v * t).Normalized();
    }

    private void Check(string what, bool ok)
    {
        GD.Print((ok ? "ok   " : "FAIL ") + what);
        if (!ok)
            _failures++;
    }
}
