using System;
using System.Collections.Generic;
using Godot;
using Xpace.Net;
using Xpace.Ship;
using Xpace.Space;

namespace Xpace.Tests;

/// <summary>
/// A ship's state across the network: the snapshot's precision, and a
/// remote ship following its owner through links with latency, jitter and
/// loss. Pure code, no frames: a minute of flight takes a moment.
/// Run: Godot --headless --path . res://tests/net_test.tscn
/// Exits with 0 if every check passed.
/// </summary>
public partial class NetTest : Node
{
    private const float Dt = 1f / 60;
    // Fixed numbers, not the hull's current tuning: the reference hash is of these
    private static readonly ShipSpec Spec = new()
    {
        MainThrust = 1_000_000f, ReverseThrust = 500_000f, LateralThrust = 500_000f, VerticalThrust = 500_000f,
        PitchAcceleration = 1.5f, RollAcceleration = 3.0f, YawAcceleration = 0.8f,
    };
    private int _failures;

    public override async void _Ready()
    {
        Snapshots();
        Follow("LAN", 0.005, 0.002, 0, 0.05f, 0.05f);
        Follow("good line", 0.05, 0.01, 0, 0.05f, 0.05f);
        Follow("average line", 0.1, 0.03, 0.05, 0.10f, 1f);
        Follow("bad wifi", 0.15, 0.05, 0.2, 0.5f, 5f);
        await InTheScene();
        GD.Print(_failures == 0 ? "ALL PASSED" : $"{_failures} FAILED");
        GetTree().Quit(_failures == 0 ? 0 : 1);
    }

    private void Snapshots()
    {
        var random = new Random(7);
        double position = 0, angle = 0, velocity = 0, rate = 0;
        int size = 0;
        bool same = true;
        for (int i = 0; i < 10000; i++)
        {
            var q = new Quaternion(Signed(random), Signed(random), Signed(random), Signed(random)).Normalized();
            var s = new ShipSnapshot
            {
                Tick = (uint)random.Next(),
                Position = new SectorPoint(Signed(random) * 2e6, Signed(random) * 2e6, Signed(random) * 2e6),
                Orientation = i % 2 == 0 ? q : -q,      // the same turn either way
                Velocity = new Vector3(Signed(random), Signed(random), Signed(random)) * 4000,
                AngularVelocity = new Vector3(Signed(random), Signed(random), Signed(random)) * 15,
            };
            byte[] bytes = s.ToBytes();
            size = Math.Max(size, bytes.Length);
            ShipSnapshot r = ShipSnapshot.Read(new BitReader(bytes));
            same &= r.Tick == s.Tick;
            position = Math.Max(position, Math.Max(Math.Abs(r.Position.X - s.Position.X),
                                Math.Max(Math.Abs(r.Position.Y - s.Position.Y), Math.Abs(r.Position.Z - s.Position.Z))));
            angle = Math.Max(angle, Mathf.RadToDeg(r.Orientation.AngleTo(q)));
            velocity = Math.Max(velocity, (r.Velocity - s.Velocity).Abs()[(int)(r.Velocity - s.Velocity).Abs().MaxAxisIndex()]);
            rate = Math.Max(rate, (r.AngularVelocity - s.AngularVelocity).Abs()[(int)(r.AngularVelocity - s.AngularVelocity).Abs().MaxAxisIndex()]);
        }
        Check($"a snapshot is {size} bytes", size == ShipSnapshot.Bytes);
        Check($"the tick comes back as it was", same);
        Check($"position within {position * 1000:0.00} mm, 2,000 km out", position <= 0.5 / ShipSnapshot.PositionScale + 1e-9);
        Check($"orientation within {angle:0.000} deg, q or -q", angle < 0.12);
        Check($"velocity within {velocity:0.000} m/s, angular velocity within {rate:0.0000} rad/s", velocity <= 4096.0 / 65535 / 2 + 1e-4 && rate <= 16.0 / 32767 / 2 + 1e-6);
    }

    /// <summary>
    /// The owner flies a minute of hard manoeuvres (stick and thrusters changing
    /// every half second, up to 5 g) 150 km out in the sector, sending 20
    /// snapshots a second; the remote ship is compared with where the owner
    /// really was at the tick it shows.
    /// </summary>
    private void Follow(string name, double latency, double jitter, double loss, float meanLimit, float worstLimit)
    {
        var link = new SimulatedLink(3) { Latency = latency, Jitter = jitter, Loss = loss };
        var track = new RemoteTrack { TickSeconds = Dt };
        var sector = new SectorPoint(150_000, -20_000, 1_000);
        var truth = new List<ShipState>();
        var owner = new ShipState { Orientation = Quaternion.Identity, Velocity = new Vector3(0, 0, -150) };

        const int Ticks = 3600, Warmup = 60;
        double sum = 0, worst = 0, worstAngle = 0, delay = 0, slowest = 1, fastest = 1;
        int samples = 0, coasting = 0;
        double? lastShown = null;
        for (uint tick = 0; tick < Ticks; tick++)
        {
            truth.Add(owner);
            if (tick % 3 == 0)
            {
                var s = new ShipSnapshot
                {
                    Tick = tick, Position = sector + owner.Position, Orientation = owner.Orientation,
                    Velocity = owner.Velocity, AngularVelocity = owner.AngularVelocity,
                };
                link.Send(s.ToBytes(), tick * Dt);
            }
            foreach (byte[] packet in link.Receive(tick * Dt))
                track.Receive(ShipSnapshot.Read(new BitReader(packet)), tick);
            track.Advance(tick);

            if (tick >= Warmup && track.ShownTick.HasValue)
            {
                double shown = track.ShownTick.Value;
                RemoteTrack.Pose p = track.Sample();
                (Vector3 at, Quaternion facing) = Truth(truth, shown);
                double error = p.Position.To(sector + at).Length();
                sum += error;
                worst = Math.Max(worst, error);
                worstAngle = Math.Max(worstAngle, Mathf.RadToDeg(p.Orientation.AngleTo(facing)));
                delay += tick - shown;
                if (p.Extrapolated)
                    coasting++;
                if (lastShown.HasValue)
                {
                    slowest = Math.Min(slowest, shown - lastShown.Value);
                    fastest = Math.Max(fastest, shown - lastShown.Value);
                }
                lastShown = shown;
                samples++;
            }

            (Vector3 force, Vector3 torque) = Commands(tick);
            owner = ShipMotion.Step(owner, force, torque, Spec.Mass, Spec.Inertia, Dt);
        }

        double mean = sum / samples;
        GD.Print($"     {name}: {latency * 1000:0} ms ±{jitter * 1000:0}, {loss * 100:0}% lost: shown {delay / samples * Dt * 1000:0} ms late, " +
                 $"coasting {coasting * 100.0 / samples:0.0}% of the time, {link.BytesSent / (Ticks * Dt):0} bytes/s");
        Check($"{name}: off by {mean * 100:0.0} cm on average, {worst * 100:0} cm and {worstAngle:0.00} deg at worst",
              mean < meanLimit && worst < worstLimit);
        Check($"{name}: the shown time runs smoothly ({slowest:0.00} to {fastest:0.00} ticks a tick)",
              slowest >= 0.94 && fastest <= 1.06);
    }

    /// <summary>
    /// The nodes: a remote ship under a floating origin, fed snapshots of a
    /// ship cruising at 100 m/s 150 km out; the origin then jumps 5 km.
    /// </summary>
    private async System.Threading.Tasks.Task InTheScene()
    {
        var origin = new FloatingOrigin();
        AddChild(origin);
        var clock = new SectorClock();
        origin.AddChild(clock);
        var remote = new RemoteShip();
        origin.AddChild(remote);
        var start = new SectorPoint(150_000, 0, 0);
        var velocity = new Vector3(0, 0, -100);

        bool shifted = false;
        float worst = 0;
        for (int frame = 0; frame < 120; frame++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            if (clock.Tick % 3 == 0)
                remote.Receive(new ShipSnapshot
                {
                    Tick = clock.Tick, Position = start + velocity * (clock.Tick * Dt),
                    Orientation = Quaternion.Identity, Velocity = velocity,
                }.ToBytes());
            if (frame == 60)
            {
                origin.Shift(new Vector3(5000, 0, 0));
                shifted = true;
                continue;                       // the node moves on the next step
            }
            if (shifted && remote.Track.ShownTick.HasValue)
            {
                // The node and its shown tick are both from the last step
                SectorPoint expected = start + velocity * (float)(remote.Track.ShownTick.Value * Dt);
                worst = Math.Max(worst, remote.GlobalPosition.DistanceTo(origin.Origin.To(expected)));
            }
        }
        Check($"in the scene the remote ship is where it was sent, across an origin shift ({worst * 1000:0.0} mm)",
              remote.Visible && shifted && worst < 0.01f);
    }

    /// <summary>Where the owner was at a tick between two steps.</summary>
    private static (Vector3, Quaternion) Truth(List<ShipState> truth, double tick)
    {
        int i = Math.Clamp((int)Math.Floor(tick), 0, truth.Count - 2);
        float u = (float)Math.Clamp(tick - i, 0, 1);
        return (truth[i].Position.Lerp(truth[i + 1].Position, u), truth[i].Orientation.Slerp(truth[i + 1].Orientation, u));
    }

    /// <summary>Stick and thrusters changing every half second, the same on every run.</summary>
    private static (Vector3 force, Vector3 torque) Commands(uint step)
    {
        uint seed = step / 30 * 2654435761u + 12345;
        float Next()
        {
            seed = seed * 1664525 + 1013904223;
            return (seed >> 8) / (float)(1 << 23) - 1;         // -1..1
        }
        var force = new Vector3(Next() * Spec.LateralThrust, Next() * Spec.VerticalThrust, -Next() * Spec.MainThrust);
        var torque = new Vector3(Next() * Spec.PitchAcceleration * Spec.Inertia.X,
                                 Next() * Spec.YawAcceleration * Spec.Inertia.Y,
                                 Next() * Spec.RollAcceleration * Spec.Inertia.Z);
        return (force, torque);
    }

    private static float Signed(Random r) => (float)(r.NextDouble() * 2 - 1);

    private void Check(string what, bool ok)
    {
        GD.Print((ok ? "ok   " : "FAIL ") + what);
        if (!ok)
            _failures++;
    }
}
