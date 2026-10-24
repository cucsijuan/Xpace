using System;
using System.Collections.Generic;
using Godot;
using Xpace.Net;
using Xpace.Space;

namespace Xpace.Tests;

/// <summary>
/// Three machines in a session over a simulated network: the host flies a
/// drone, guest 1 shoots at it, guest 2 flies by and watches. Every machine
/// runs its own Session. Pure code, no frames.
/// Run: Godot --headless --path . res://tests/session_test.tscn
/// Exits with 0 if every check passed.
/// </summary>
public partial class SessionTest : Node
{
    private const float Dt = 1f / 60;
    private const float MuzzleSpeed = 1200;
    private const byte Host = 0, Guest1 = 1, Guest2 = 2, DroneId = 16;
    private static readonly SectorPoint Sector = new(150_000, 0, 0);
    private static readonly Hitbox Box = new(Vector3.Zero, new Vector3(5, 3, 10));
    private int _failures;

    /// <summary>A ship as one machine has it: the true one if it owns it, else a remote copy.</summary>
    private sealed class Fake : INetEntity
    {
        public byte NetId { get; init; }
        public bool IsLocal { get; init; }
        public RemoteTrack Track { get; init; }
        public Hitbox[] Hitboxes => new[] { Box };
        public bool Hittable => true;
        public SectorPoint Position;
        public Vector3 Velocity;
        public float Damage;
        public int Hits;

        public ShipSnapshot Snapshot(uint tick) => new()
        {
            Tick = tick, Position = Position, Orientation = Quaternion.Identity, Velocity = Velocity,
        };

        public void TakeHit(HitEvent hit)
        {
            Damage += hit.Damage;
            Hits++;
        }
    }

    public override void _Ready()
    {
        var net = new SimulatedNetwork { Latency = 0.06, Jitter = 0.02, Loss = 0.05 };
        var machines = new Dictionary<byte, Session>();
        var copies = new Dictionary<(byte machine, byte ship), Fake>();
        var heard = new Dictionary<byte, int>();
        var drawn = new Dictionary<byte, int>();
        foreach (byte peer in new[] { Host, Guest1, Guest2 })
        {
            byte machine = peer;
            var session = new Session(peer, net.Join(peer)) { TickSeconds = Dt };
            session.SpawnRemote = id =>
            {
                var copy = new Fake { NetId = id, Track = new RemoteTrack { TickSeconds = Dt } };
                copies[(machine, id)] = copy;
                return copy;
            };
            heard[peer] = drawn[peer] = 0;
            session.HitConfirmed += _ => heard[machine]++;
            session.RoundFired += _ => drawn[machine]++;
            machines[peer] = session;
        }

        // Who owns what: the drone crosses 1 km in front of guest 1; guest 2 flies past
        var drone = new Fake { NetId = DroneId, IsLocal = true, Position = Sector + new Vector3(-600, 0, -1000), Velocity = new Vector3(200, 0, 0) };
        var ship1 = new Fake { NetId = Guest1, IsLocal = true, Position = Sector };
        var ship2 = new Fake { NetId = Guest2, IsLocal = true, Position = Sector + new Vector3(0, 300, -400), Velocity = new Vector3(0, 0, 80) };
        machines[Host].Add(drone);
        machines[Guest1].Add(ship1);
        machines[Guest2].Add(ship2);

        var flying = new List<FireEvent>();
        var seen = new HashSet<ushort>();
        int fired = 0, hostFired = 0;
        RemoteTrack.Pose previous = default;
        double relayError = 0;
        int relaySamples = 0;

        for (uint tick = 0; tick < 900; tick++)
        {
            net.Now = tick * Dt;
            foreach (Session s in machines.Values)
                s.Tick(tick);

            // Guest 1 flies its rounds against what it shows, and fires at the drone it sees
            if (copies.TryGetValue((Guest1, DroneId), out Fake seenDrone) && seenDrone.Track.ShownTick.HasValue)
            {
                RemoteTrack.Pose now = seenDrone.Track.Sample();
                for (int i = flying.Count - 1; i >= 0; i--)
                {
                    FireEvent e = flying[i];
                    int n = (int)(tick - e.Tick) - 1;
                    if (n >= 0 && Box.Swept(e.At(n, Dt), e.At(n + 1, Dt), previous, now))
                    {
                        seen.Add(e.Sequence);
                        flying.RemoveAt(i);
                    }
                    else if (n > 240)
                        flying.RemoveAt(i);
                }
                previous = now;

                Vector3 p = Sector.To(now.Position), v = now.Velocity;
                if (tick % 6 == 0 && MathF.Abs(p.X) < 800)
                {
                    flying.Add(machines[Guest1].Fire(tick, Sector, Lead(p, v) * MuzzleSpeed, 10));
                    fired++;
                }
            }

            // The host's gun, 600 m below guest 1, fires a round a second at the guest it sees
            if (copies.TryGetValue((Host, Guest1), out Fake seenShip) && seenShip.Track.ShownTick.HasValue && tick % 60 == 0)
            {
                SectorPoint gun = Sector + new Vector3(0, -600, 0);
                RemoteTrack.Pose p = seenShip.Track.Sample();
                machines[Host].Fire(tick, gun, Lead(gun.To(p.Position), p.Velocity) * MuzzleSpeed, 5);
                hostFired++;
            }

            // Guest 2 sees guest 1's ship through the host
            if (copies.TryGetValue((Guest2, Guest1), out Fake relayed) && relayed.Track.ShownTick.HasValue)
            {
                relayError += relayed.Track.Sample().Position.To(Sector).Length();
                relaySamples++;
            }

            drone.Position += drone.Velocity * Dt;
            ship2.Position += ship2.Velocity * Dt;
        }

        Check($"guest 1 saw {seen.Count} of its {fired} rounds hit the drone; the drone took {drone.Hits} on the host",
              seen.Count > fired / 2 && drone.Hits >= seen.Count * 95 / 100 && drone.Hits <= seen.Count + seen.Count / 20);
        Check($"the drone's damage is the rounds' ({drone.Damage:0})", Math.Abs(drone.Damage - drone.Hits * 10) < 0.01);
        Check($"the host's {hostFired} rounds hit guest 1's ship, on guest 1 ({ship1.Hits} hits, {ship1.Damage:0} damage)",
              ship1.Hits >= hostFired * 9 / 10 && Math.Abs(ship1.Damage - ship1.Hits * 5) < 0.01);
        int total = drone.Hits + ship1.Hits;
        Check($"everyone hears every hit (host {heard[Host]}, guest 1 {heard[Guest1]}, guest 2 {heard[Guest2]} of {total})",
              heard[Host] == total && heard[Guest1] == total && heard[Guest2] == total);
        Check($"remote copies take no damage", copies[(Guest1, DroneId)].Damage == 0 && copies[(Host, Guest1)].Damage == 0);
        Check($"guest 2 draws guest 1's {fired} rounds and the host's {hostFired} ({drawn[Guest2]}), guest 1 the host's ({drawn[Guest1]})",
              drawn[Guest2] == fired + hostFired && drawn[Guest1] == hostFired && drawn[Host] == fired);
        Check($"guest 2 sees guest 1's ship through the host ({relayError / Math.Max(1, relaySamples) * 1000:0.0} mm off)",
              relaySamples > 600 && relayError / relaySamples < 0.01);
        Check("guest 2's ship, flying by, was never hit", ship2.Hits == 0);

        GD.Print(_failures == 0 ? "ALL PASSED" : $"{_failures} FAILED");
        GetTree().Quit(_failures == 0 ? 0 : 1);
    }

    /// <summary>The direction to fire at a target at <paramref name="p"/> moving at <paramref name="v"/>, from a gun at rest.</summary>
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
