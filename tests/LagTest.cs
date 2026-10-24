using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Xpace.Net;
using Xpace.Ship;
using Xpace.Space;

namespace Xpace.Tests;

/// <summary>
/// Rounds across the network: a shooter (A) fires at another player's ship
/// (B) as A sees it, and the host judges every round with lag compensation.
/// What A saw hit must be what the host says hit. Pure code, no frames.
/// Run: Godot --headless --path . res://tests/lag_test.tscn
/// Exits with 0 if every check passed.
/// </summary>
public partial class LagTest : Node
{
    private const float Dt = 1f / 60;
    private const float MuzzleSpeed = 1200;
    private const byte IdA = 1, IdB = 2;
    private static readonly ShipSpec Spec = new();
    private static readonly SectorPoint Sector = new(150_000, 0, 0);
    private static readonly Hitbox Box = new(Vector3.Zero, new Vector3(5, 3, 10));
    private int _failures;

    private struct Result
    {
        public int Fired, SeenHit, Judged, Confirmed, Extra;
        public double MovedOn;              // metres B had moved on its own screen, on average, when hit
        public double Confirm => SeenHit == 0 ? 0 : (double)Confirmed / SeenHit;
    }

    public override void _Ready()
    {
        Pieces();

        Result r = Duel(jink: false, eventLatency: 0.06, compensate: true);
        Check($"crossing at 200 m/s: the host confirms {r.Confirm * 100:0}% of the {r.SeenHit} hits A saw, {r.Extra} more " +
              $"(B had moved {r.MovedOn:0.0} m on)", r.SeenHit > r.Fired / 2 && r.Confirm >= 0.95 && r.Extra <= r.SeenHit / 20);

        r = Duel(jink: false, eventLatency: 0.06, compensate: false);
        Check($"without lag compensation it confirms {r.Confirm * 100:0}% of them", r.Confirm < 0.2);

        r = Duel(jink: true, eventLatency: 0.06, compensate: true);
        Check($"against B jinking at 2 g: A sees {r.SeenHit} of {r.Fired} hit, the host confirms {r.Confirm * 100:0}%, {r.Extra} more",
              r.Confirm >= 0.9 && r.Extra <= r.SeenHit / 10);

        r = Duel(jink: false, eventLatency: 0.4, compensate: true);
        Check($"fire events 400 ms late: the host still confirms {r.Confirm * 100:0}%", r.Confirm >= 0.95);

        r = Duel(jink: false, eventLatency: 0.06, compensate: true, extraDelay: 40);
        Check($"A showing B 0.75 s late, past the 0.5 s rewind: {r.Confirm * 100:0}% confirmed", r.Confirm < 0.2);

        GD.Print(_failures == 0 ? "ALL PASSED" : $"{_failures} FAILED");
        GetTree().Quit(_failures == 0 ? 0 : 1);
    }

    private void Pieces()
    {
        var half = new Vector3(1, 1, 1);
        Check("a round through the box hits, one beside it misses, one stopping short misses",
              Hitbox.Segment(new Vector3(-5, 0.5f, 0), new Vector3(5, 0.5f, 0), half) &&
              !Hitbox.Segment(new Vector3(-5, 1.5f, 0), new Vector3(5, 1.5f, 0), half) &&
              !Hitbox.Segment(new Vector3(-5, 0, 0), new Vector3(-2, 0, 0), half) &&
              Hitbox.Segment(new Vector3(0, 0, 0.5f), new Vector3(0, 0, 0.6f), half));

        // A ship crossing a round's step: at no instant is the round inside it, but they meet
        var before = new RemoteTrack.Pose { Position = Sector + new Vector3(-10, 0, 0), Orientation = Quaternion.Identity };
        var after = new RemoteTrack.Pose { Position = Sector + new Vector3(10, 0, 0), Orientation = Quaternion.Identity };
        Check("a ship and a round crossing within one step meet",
              Box.Swept(Sector + new Vector3(0, 0, -20), Sector + new Vector3(0, 0, 20), before, after));

        var e = new FireEvent
        {
            Shooter = 3, Sequence = 65000, Tick = 123456789, Origin = new SectorPoint(-1_500_000.25, 2, 3),
            Velocity = new Vector3(1200, -30, 5), Damage = 12.5f,
            Views = new() { new() { Ship = 1, DelayTicks = 12.3 }, new() { Ship = 9, DelayTicks = 0 } },
        };
        byte[] bytes = e.ToBytes();
        FireEvent back = FireEvent.Read(new BitReader(bytes));
        Check($"a fire event ({bytes.Length} bytes with two views) comes back as it went",
              back.Shooter == 3 && back.Sequence == 65000 && back.Tick == e.Tick && back.Origin.To(e.Origin).Length() < 0.001f &&
              (back.Velocity - e.Velocity).Length() < 0.06f && back.Damage == 12.5f && back.Views.Count == 2 &&
              back.Views[1].Ship == 9 && Math.Abs(back.Views[0].DelayTicks - 12.3) < 0.003);
        var h = new HitEvent { Shooter = 3, Sequence = 7, Target = 1, Tick = 99, Damage = 12.5f };
        HitEvent hb = HitEvent.Read(new BitReader(h.ToBytes()));
        Check("a hit event too", hb.Shooter == 3 && hb.Sequence == 7 && hb.Target == 1 && hb.Tick == 99 && hb.Damage == 12.5f);

        // A wing-like box, 10 m by 0.2 m, turned 45 degrees about Z: a round along X at y = 3 crosses it (at x = 3), and misses it unturned
        var still = new RemoteTrack.Pose { Orientation = Quaternion.Identity };
        var turned = new Hitbox(Vector3.Zero, new Vector3(10, 0.2f, 1), new Basis(Vector3.Back, Mathf.Pi / 4));
        var flat = new Hitbox(Vector3.Zero, new Vector3(10, 0.2f, 1));
        SectorPoint from = new(-10, 3, 0), to = new(10, 3, 0);
        Check("a turned hitbox is hit where it lies, not where it would unturned",
              turned.Swept(from, to, still, still) && !flat.Swept(from, to, still, still) &&
              !turned.Swept(new SectorPoint(-10, -3, 0), new SectorPoint(-4, -3, 0), still, still));

        // The remote fighter: its boxes from the model's collision, turned with the model
        var fighter = GD.Load<PackedScene>("res://scenes/ships/remote_fighter.tscn").Instantiate<RemoteShip>();
        AddChild(fighter);
        Hitbox[] boxes = fighter.Hitboxes;
        int plumes = fighter.FindChildren("*", "", true, false).OfType<EnginePlume>().Count();
        bool noseAhead = boxes.Min(b => b.Centre.Z) < -4 && boxes.Max(b => b.Centre.Z) > 3;
        Check($"the remote fighter has its {boxes.Length} hitboxes (the screen and radar make none), some turned, nose ahead, and {plumes} plumes",
              boxes.Length == 23 && boxes.Any(b => !b.ToBox.IsEqualApprox(Basis.Identity)) && noseAhead && plumes == 2);
        fighter.QueueFree();
    }

    /// <summary>
    /// B crosses 1 km in front of A at 200 m/s (or jinks at 2 g about that
    /// path); A fires 10 rounds a second at what it sees, with a perfect lead
    /// on what it sees. B's snapshots reach A and the host on their own lines.
    /// </summary>
    private static Result Duel(bool jink, double eventLatency, bool compensate, double extraDelay = 0)
    {
        var toA = new SimulatedLink(11) { Latency = 0.08, Jitter = 0.02, Loss = 0.05 };
        var toHost = new SimulatedLink(12) { Latency = 0.06, Jitter = 0.02, Loss = 0.05 };
        var events = new SimulatedLink(13) { Latency = eventLatency, Jitter = 0.02, Loss = 0 };   // reliable
        var seenByA = new RemoteTrack { TickSeconds = Dt, SafetyTicks = 1 + extraDelay };
        var atHost = new RemoteTrack { TickSeconds = Dt };
        var judge = new LagCompensator { TickSeconds = Dt, Compensate = compensate };
        judge.Register(IdB, atHost, Box);

        var result = new Result();
        var seenHits = new HashSet<ushort>();
        var judged = new Dictionary<ushort, uint>();
        judge.Hit += h => judged[h.Sequence] = h.Tick;
        var flying = new List<FireEvent>();
        var truth = new List<ShipState>();
        var rewinds = new Dictionary<ushort, double>();
        var b = new ShipState
        {
            Position = new Vector3(-600, 0, -1000), Orientation = Quaternion.Identity, Velocity = new Vector3(200, 0, 0),
        };
        RemoteTrack.Pose previous = default;
        ushort sequence = 0;

        for (uint tick = 0; tick < 900; tick++)
        {
            truth.Add(b);
            if (tick % 3 == 0)
            {
                byte[] snapshot = new ShipSnapshot
                {
                    Tick = tick, Position = Sector + b.Position, Orientation = b.Orientation,
                    Velocity = b.Velocity, AngularVelocity = b.AngularVelocity,
                }.ToBytes();
                toA.Send(snapshot, tick * Dt);
                toHost.Send(snapshot, tick * Dt);
            }
            foreach (byte[] p in toA.Receive(tick * Dt))
                seenByA.Receive(ShipSnapshot.Read(new BitReader(p)), tick);
            foreach (byte[] p in toHost.Receive(tick * Dt))
                atHost.Receive(ShipSnapshot.Read(new BitReader(p)), tick);
            seenByA.Advance(tick);

            if (seenByA.ShownTick.HasValue)
            {
                // A flies its own rounds against what it shows
                RemoteTrack.Pose now = seenByA.Sample();
                for (int i = flying.Count - 1; i >= 0; i--)
                {
                    FireEvent e = flying[i];
                    int n = (int)(tick - e.Tick) - 1;
                    if (n >= 0 && Box.Swept(e.At(n, Dt), e.At(n + 1, Dt), previous, now))
                    {
                        seenHits.Add(e.Sequence);
                        flying.RemoveAt(i);
                    }
                    else if (n > 240)
                        flying.RemoveAt(i);
                }
                previous = now;

                // and fires, leading the target it sees
                Vector3 p = Sector.To(now.Position), v = now.Velocity;
                if (tick % 6 == 0 && MathF.Abs(p.X) < 800)
                {
                    float qa = v.Dot(v) - MuzzleSpeed * MuzzleSpeed, qb = 2 * p.Dot(v), qc = p.Dot(p);
                    float t = (-qb - MathF.Sqrt(qb * qb - 4 * qa * qc)) / (2 * qa);
                    double delay = tick - seenByA.ShownTick.Value;
                    var fired = new FireEvent
                    {
                        Shooter = IdA, Sequence = sequence++, Tick = tick, Origin = Sector,
                        Velocity = (p + v * t).Normalized() * MuzzleSpeed, Damage = 10,
                        Views = new() { new() { Ship = IdB, DelayTicks = delay } },
                    };
                    byte[] bytes = fired.ToBytes();
                    FireEvent sent = FireEvent.Read(new BitReader(bytes));      // A flies what it sent
                    flying.Add(sent);
                    rewinds[sent.Sequence] = Math.Min(delay, judge.MaxRewindTicks);
                    events.Send(bytes, tick * Dt);
                    result.Fired++;
                }
            }

            foreach (byte[] p in events.Receive(tick * Dt))
                judge.Fire(FireEvent.Read(new BitReader(p)));
            judge.Step(tick);

            (Vector3 force, Vector3 torque) = jink ? Jink(tick) : default;
            b = ShipMotion.Step(b, force, torque, Spec.Mass, Spec.Inertia, Dt);
        }

        result.SeenHit = seenHits.Count;
        result.Judged = judged.Count;
        double moved = 0;
        foreach ((ushort seq, uint at) in judged)
        {
            if (seenHits.Contains(seq))
                result.Confirmed++;
            else
                result.Extra++;
            int then = Math.Clamp((int)Math.Round(at - rewinds[seq]), 0, truth.Count - 1);
            moved += truth[Math.Min((int)at, truth.Count - 1)].Position.DistanceTo(truth[then].Position);
        }
        result.MovedOn = judged.Count == 0 ? 0 : moved / judged.Count;
        return result;
    }

    /// <summary>Up to 2 g sideways and up and down, and some turning, changing every half second.</summary>
    private static (Vector3 force, Vector3 torque) Jink(uint step)
    {
        uint seed = step / 30 * 2654435761u + 999;
        float Next()
        {
            seed = seed * 1664525 + 1013904223;
            return (seed >> 8) / (float)(1 << 23) - 1;
        }
        float g = 2 * 9.81f * Spec.Mass;
        return (new Vector3(Next() * g, Next() * g, 0),
                new Vector3(Next() * 0.3f * Spec.Inertia.X, Next() * 0.3f * Spec.Inertia.Y, Next() * 0.3f * Spec.Inertia.Z));
    }

    private void Check(string what, bool ok)
    {
        GD.Print((ok ? "ok   " : "FAIL ") + what);
        if (!ok)
            _failures++;
    }
}
