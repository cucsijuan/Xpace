using System;
using System.Threading.Tasks;
using Godot;
using Xpace.Net;
using Xpace.Space;

namespace Xpace.Tests;

/// <summary>
/// The session inside the game: this scene is the host (a SessionNode, the
/// rounds, a real drone), and a guest runs beside it as plain code over a
/// simulated network. The guest shoots the host's drone; the host's gun
/// shoots the guest's ship, which appears here as a RemoteShip.
/// Run: Godot --headless --fixed-fps 60 --path . res://tests/session_scene_test.tscn
/// Exits with 0 if every check passed.
/// </summary>
public partial class SessionSceneTest : Node
{
    private const float Dt = 1f / 60;
    private const float MuzzleSpeed = 1200;
    private const byte GuestPeer = 1, DroneId = SessionNode.FirstDroneId;
    private static readonly Hitbox Box = new(Vector3.Zero, new Vector3(5, 3, 10));
    private int _failures;

    private sealed class GuestShip : INetEntity
    {
        public byte NetId { get; init; }
        public bool IsLocal { get; init; }
        public RemoteTrack Track { get; init; }
        public Hitbox[] Hitboxes => new[] { Box };
        public bool Hittable => true;
        public int Hits;

        public ShipSnapshot Snapshot(uint tick) => new() { Tick = tick, Orientation = Quaternion.Identity };

        public void TakeHit(HitEvent hit) => Hits++;
    }

    public override async void _Ready()
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
        origin.AddChild(drone);
        var host = new SessionNode { ShipPath = "" };
        origin.AddChild(host);

        var net = new SimulatedNetwork { Latency = 0.06, Jitter = 0.02, Loss = 0.05 };
        host.Start(Session.HostPeer, net.Join(Session.HostPeer));
        var guest = new Session(GuestPeer, net.Join(GuestPeer)) { TickSeconds = Dt };
        var ship = new GuestShip { NetId = GuestPeer, IsLocal = true };
        guest.Add(ship);
        GuestShip seenDrone = null;
        guest.SpawnRemote = id => id == DroneId ? seenDrone = new GuestShip { NetId = id, Track = new RemoteTrack { TickSeconds = Dt } } : null;
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
                    guest.Fire(tick, default, Lead(at, p.Velocity) * MuzzleSpeed, 10);
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

        GD.Print(_failures == 0 ? "ALL PASSED" : $"{_failures} FAILED");
        GetTree().Quit(_failures == 0 ? 0 : 1);
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
